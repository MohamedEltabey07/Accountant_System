using BLL.DTOs.Inventory.InComingData;
using BLL.DTOs.Inventory.OutGoingData;
using BLL.DTOs.Product.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.Enums;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BLL.Service
{
    // Frozen fish inventory ledger service. StockMovements are immutable and are
    // the single source of truth: every available quantity / weight is derived by
    // summing the ledger. Manual operations are guarded from negative stock and
    // serialized against the maritime posting flow through StockConcurrencyGate.
    public class StockMovementService : IStockMovementService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IProductService _productService;

        public StockMovementService(IUnitOfWork unitOfWork, IProductService productService)
        {
            _unitOfWork = unitOfWork;
            _productService = productService;
        }

        public async Task<List<StockBalanceDto>> GetStockBalancesAsync()
        {
            var summaries = await _unitOfWork.StockMovementRepo.GetFrozenFishBalancesAsync();
            var activeIds = (await _productService.GetProductsByCategoryAsync(ProductCategory.FrozenFish))
                .Select(p => p.Id)
                .ToHashSet();

            return summaries
                .Select(s => ToBalanceDto(s, activeIds.Contains(s.ProductId)))
                .OrderBy(b => b.ProductName)
                .ToList();
        }

        public async Task<StockBalanceDto?> GetBalanceAsync(int productId)
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null || product.Category != ProductCategory.FrozenFish)
                return null;

            var summary = await _unitOfWork.StockMovementRepo.GetBalanceForProductAsync(productId);
            if (summary == null)
            {
                return new StockBalanceDto
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    IsActive = product.IsActive,
                    DefaultCartonWeightKg = product.DefaultCartonWeightKg,
                    MaritimePricePerKg = product.MaritimePricePerKg
                };
            }

            return ToBalanceDto(summary, product.IsActive);
        }

        public async Task<InventoryDashboardDto> GetInventoryDashboardAsync(DateTime? from, DateTime? to)
        {
            ValidateRange(from, to);
            var balances = await GetStockBalancesAsync();
            var movements = await _unitOfWork.StockMovementRepo.GetFilteredAsync(null, null, from, to);

            return new InventoryDashboardDto
            {
                IncomingFromDate = from,
                IncomingToDate = to,
                FrozenFishItemsCount = balances.Count,
                OutOfStockItemsCount = balances.Count(b => b.IsOutOfStock),
                TotalAvailableCartons = balances.Sum(b => b.AvailableCartons),
                TotalAvailableKg = balances.Sum(b => b.AvailableKg),
                TotalIncomingKg = movements.Where(m => m.MovementType == StockMovementType.In)
                                           .Sum(m => m.TotalWeightKg),
                TotalIncomingCartons = movements.Where(m => m.MovementType == StockMovementType.In)
                                                .Sum(m => m.Cartons),
                TotalOutgoingKg = movements.Where(m => m.TotalWeightKg < 0)
                                           .Sum(m => -m.TotalWeightKg),
                TotalOutgoingCartons = movements.Where(m => m.Cartons < 0)
                                                .Sum(m => -m.Cartons),
                Balances = balances,
                OutOfStockItems = balances.Where(b => b.IsOutOfStock).ToList()
            };
        }

        public async Task<List<StockMovementDto>> GetMovementsAsync(int? productId, StockMovementType? movementType, DateTime? from, DateTime? to)
        {
            ValidateRange(from, to);
            var movements = await _unitOfWork.StockMovementRepo.GetFilteredAsync(productId, movementType, from, to);
            return movements.Select(ToMovementDto).ToList();
        }

        public async Task<List<ProductDto>> GetFrozenFishProductsAsync()
        {
            return await _productService.GetProductsByCategoryAsync(ProductCategory.FrozenFish);
        }

        public async Task RecordStockInAsync(CreateStockInDto dto, string? userId = null)
        {
            if (dto == null)
                throw new InvalidOperationException("بيانات الاستلام لا يمكن أن تكون فارغة.");
            ValidateMovementDate(dto.MovementDate, "الاستلام");

            await StockConcurrencyGate.Gate.WaitAsync();
            try
            {
                var (product, perCartonWeight) = await GetFrozenFishProductAsync(dto.ProductId);
                perCartonWeight = ResolvePerCartonWeight(perCartonWeight, dto.WeightPerCartonKg);

                var movement = new StockMovement
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    MovementType = StockMovementType.In,
                    MovementDate = dto.MovementDate,
                    Cartons = Round(dto.Cartons),
                    WeightPerCartonKg = perCartonWeight,
                    TotalWeightKg = Round(dto.Cartons * perCartonWeight),
                    Reason = dto.Notes,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = userId
                };

                await PersistMovementAsync(movement);
            }
            finally
            {
                StockConcurrencyGate.Gate.Release();
            }
        }

        public async Task RecordStockOutAsync(CreateStockOutDto dto, string? userId = null)
        {
            if (string.IsNullOrWhiteSpace(dto?.Reason))
                throw new InvalidOperationException("سبب الخروج مطلوب لكل عملية سحب من الثلاجات.");
            if (dto.WeightKg <= 0)
                throw new InvalidOperationException("وزن الخروج يجب أن يكون أكبر من صفر.");
            ValidateMovementDate(dto.MovementDate, "الخروج");

            await StockConcurrencyGate.Gate.WaitAsync();
            try
            {
                var (product, defaultPerCarton, availableKg) = await GetStockStateAsync(dto.ProductId);

                if (dto.WeightKg > availableKg)
                    throw new InvalidOperationException(
                        $"لا يمكن إخراج {dto.WeightKg:N3} كجم من ({product.Name}). الكميّة المتاحة في الثلاجات لا تكفي ({availableKg:N3} كجم).");

                // Cartons: explicitly entered, otherwise derived from weight / carton weight.
                var cartons = dto.Cartons
                    ?? (defaultPerCarton > 0 ? Round(dto.WeightKg / defaultPerCarton) : 0m);

                var movement = new StockMovement
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    MovementType = StockMovementType.Out,
                    MovementDate = dto.MovementDate,
                    Cartons = -Round(cartons),
                    WeightPerCartonKg = ResolveMovementPerCartonWeight(dto.WeightKg, cartons, defaultPerCarton),
                    TotalWeightKg = -Round(dto.WeightKg),
                    Reason = dto.Reason,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = userId
                };

                await PersistMovementAsync(movement);
            }
            finally
            {
                StockConcurrencyGate.Gate.Release();
            }
        }

        public async Task RecordAdjustmentAsync(CreateStockAdjustmentDto dto, string? userId = null)
        {
            if (dto == null)
                throw new InvalidOperationException("بيانات التسوية لا يمكن أن تكون فارغة.");

            var cartonsAdjustment = dto.CartonsAdjustment ?? 0m;
            var weightAdjustment = dto.WeightKgAdjustment ?? 0m;

            if (cartonsAdjustment == 0 && weightAdjustment == 0)
                throw new InvalidOperationException("يجب إدخال تسوية للكراتين أو للوزن.");
            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new InvalidOperationException("سبب التسوية مطلوب (مثلاً: تسوية الجرد الفعلي).");
            ValidateMovementDate(dto.MovementDate, "التسوية");

            await StockConcurrencyGate.Gate.WaitAsync();
            try
            {
                var (product, _) = await GetFrozenFishProductAsync(dto.ProductId);

                var balance = await _unitOfWork.StockMovementRepo.GetBalanceForProductAsync(dto.ProductId)
                    ?? throw new InvalidOperationException("تعذر حساب رصيد الصنف.");

                // The freezer can never hold less than zero.
                if (balance.AvailableCartons + cartonsAdjustment < 0)
                    throw new InvalidOperationException(
                        $"تسوية الكراتين غير متاحة: الرصيد الحالي ({balance.AvailableCartons:N3} كرتونة) لا يسمح بخصم {Math.Abs(cartonsAdjustment):N3} كرتونة.");

                if (balance.AvailableKg + weightAdjustment < 0)
                    throw new InvalidOperationException(
                        $"تسوية الوزن غير متاحة: الرصيد الحالي ({balance.AvailableKg:N3} كجم) لا يسمح بخصم {Math.Abs(weightAdjustment):N3} كجم.");

                var movement = new StockMovement
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    MovementType = StockMovementType.Adjustment,
                    MovementDate = dto.MovementDate,
                    Cartons = Round(cartonsAdjustment),
                    WeightPerCartonKg = (cartonsAdjustment != 0 && weightAdjustment != 0)
                        ? Round(Math.Abs(weightAdjustment) / Math.Abs(cartonsAdjustment))
                        : null,
                    TotalWeightKg = Round(weightAdjustment),
                    Reason = dto.Reason,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = userId
                };

                await PersistMovementAsync(movement);
            }
            finally
            {
                StockConcurrencyGate.Gate.Release();
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        public static decimal Round(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

        private static void ValidateRange(DateTime? from, DateTime? to)
        {
            if (from.HasValue && to.HasValue && from > to)
                throw new InvalidOperationException("نطاق التاريخ غير صحيح: تاريخ البداية بعد تاريخ النهاية.");
        }

        private static void ValidateMovementDate(DateTime movementDate, string operationName)
        {
            if (movementDate.Date > DateTime.Today)
                throw new InvalidOperationException($"تاريخ حركة {operationName} لا يمكن أن يكون في المستقبل.");
        }

        private static StockBalanceDto ToBalanceDto(StockBalanceSummary summary, bool isActive)
        {
            return new StockBalanceDto
            {
                ProductId = summary.ProductId,
                ProductName = summary.ProductName,
                IsActive = isActive,
                TotalInCartons = summary.TotalInCartons,
                TotalInKg = summary.TotalInKg,
                TotalOutCartons = summary.TotalOutCartons,
                TotalOutKg = summary.TotalOutKg,
                AvailableCartons = summary.AvailableCartons,
                AvailableKg = summary.AvailableKg,
                DefaultCartonWeightKg = summary.DefaultCartonWeightKg,
                MaritimePricePerKg = summary.MaritimePricePerKg
            };
        }

        internal static StockMovementDto ToMovementDto(StockMovement movement)
        {
            return new StockMovementDto
            {
                Id = movement.Id,
                ProductId = movement.ProductId,
                ProductName = movement.ProductName,
                MovementType = movement.MovementType,
                MovementDate = movement.MovementDate,
                Cartons = Math.Abs(movement.Cartons),
                WeightPerCartonKg = movement.WeightPerCartonKg,
                TotalWeightKg = Math.Abs(movement.TotalWeightKg),
                SourceInvoiceId = movement.SourceInvoiceId,
                SourceInvoiceNumber = movement.SourceInvoice?.InvoiceNumber,
                Reason = movement.Reason,
                CreatedBy = movement.CreatedByUser?.UserName,
                CreatedAt = movement.CreatedAt
            };
        }

        private static decimal ResolvePerCartonWeight(decimal? defaultWeight, decimal? overrideWeight)
        {
            var resolved = overrideWeight ?? defaultWeight;

            if (resolved is null || resolved <= 0)
                throw new InvalidOperationException(
                    "وزن الكرتونة غير معروف لهذا الصنف. حدد وزن الكرتونة (كجم) في دليل المنتجات أو في الحركة الحالية.");

            return resolved.Value;
        }

        private static decimal? ResolveMovementPerCartonWeight(decimal weightKg, decimal cartons, decimal fallbackPerCarton)
        {
            if (cartons > 0)
                return Round(weightKg / cartons);

            return Round(fallbackPerCarton);
        }

        // Validates the product exists, is an active frozen fish item, and returns
        // the product with its default carton weight.
        private async Task<(Product Product, decimal DefaultPerCartonWeight)> GetFrozenFishProductAsync(int productId)
        {
            var product = await _unitOfWork.ProductRepo.GetByIdAsync(productId)
                ?? throw new InvalidOperationException("الصنف المحدد غير موجود.");

            if (product.Category != ProductCategory.FrozenFish)
                throw new InvalidOperationException($"الصنف ({product.Name}) ليس من أصناف الأسماك المجمدة، لا تتم متابعته في مخزون الثلاجات.");

            if (!product.IsActive)
                throw new InvalidOperationException($"الصنف ({product.Name}) غير نشط، لا يمكن إنشاء حركات مخزون له.");

            return (product, product.DefaultCartonWeightKg ?? 0m);
        }

        private async Task<(Product Product, decimal DefaultPerCartonWeight, decimal AvailableKg)> GetStockStateAsync(int productId)
        {
            var (product, perCartonWeight) = await GetFrozenFishProductAsync(productId);
            var balance = await _unitOfWork.StockMovementRepo.GetBalanceForProductAsync(productId);
            return (product, perCartonWeight, balance?.AvailableKg ?? 0m);
        }

        private async Task PersistMovementAsync(StockMovement movement)
        {
            await _unitOfWork.StockMovementRepo.AddAsync(movement);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
