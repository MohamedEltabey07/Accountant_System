using AutoMapper;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Invoice.OutGoingData;
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
    public class InvoiceService : IInvoiceService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IProductService _productService;

        public InvoiceService(IUnitOfWork unitOfWork, IMapper mapper, IProductService productService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _productService = productService;
        }

        public async Task<int> CreateDraftInvoiceAsync(CreateInvoiceDraftDto dto)
        {
            if (dto == null) return 0;

            var isValidType = dto.Type switch
            {
                InvoiceType.VendorPurchase => dto.VendorId.HasValue && !dto.CustomerId.HasValue,
                InvoiceType.WholesaleSales => dto.CustomerId.HasValue && !dto.VendorId.HasValue,
                InvoiceType.StoreCashSales => !dto.CustomerId.HasValue && !dto.VendorId.HasValue,
                InvoiceType.MaritimeSupply => dto.CustomerId.HasValue && !dto.VendorId.HasValue,
                _ => false
            };
            if (!isValidType) return 0;

            // Party-vs-invoice-type validation: wholesale invoices bill wholesale
            // customers only; maritime supply bills maritime companies only.
            if (dto.CustomerId.HasValue)
            {
                var customer = await _unitOfWork.CustomerRepo.GetByIdAsync(dto.CustomerId.Value)
                    ?? throw new InvalidOperationException("العميل المحدد غير موجود في النظام.");

                if (!customer.IsActive)
                    throw new InvalidOperationException($"العميل ({customer.Name}) غير نشط ولا يمكن إصدار فواتير له.");

                var requiredType = dto.Type == InvoiceType.MaritimeSupply
                    ? CustomerType.MaritimeCompany
                    : CustomerType.WholesaleCustomer;

                if (customer.Type != requiredType)
                {
                    throw new InvalidOperationException(dto.Type == InvoiceType.MaritimeSupply
                        ? $"فاتورة التوريد للشركات البحرية لا يمكن إصدارها للعميل ({customer.Name}) لأنه عميل جملة وليس شركة بحرية."
                        : $"فواتير مبيعات الجملة لا يمكن إصدارها لـ ({customer.Name}) لأنه شركة بحرية وليس عميل جملة.");
                }
            }

            // Maritime supply meta data: external order identification is mandatory
            // and unique (DB filtered unique index backs this validation up).
            string NormalizeExternalId(string? externalOrderNumber) => string.IsNullOrWhiteSpace(externalOrderNumber) ? string.Empty : externalOrderNumber.Trim();

            string externalOrderNumber = NormalizeExternalId(dto.ExternalOrderNumber);
            if (dto.Type == InvoiceType.MaritimeSupply)
            {
                if (string.IsNullOrEmpty(externalOrderNumber))
                    throw new InvalidOperationException("رقم أمر التوريد الخارجي المرسل من الشركة البحرية مطلوب.");
                if (!dto.SupplyOrderDate.HasValue)
                    throw new InvalidOperationException("تاريخ أمر التوريد (الرد) مطلوب.");

                var duplicateExternal = await _unitOfWork.InvoiceRepo.GetAllAsync();
                var duplicated = duplicateExternal.Any(i =>
                    i.Type == InvoiceType.MaritimeSupply &&
                    i.Status != InvoiceStatus.Cancelled &&
                    i.ExternalOrderNumber == externalOrderNumber);
                if (duplicated)
                    throw new InvalidOperationException($"رقم أمر التوريد ({externalOrderNumber}) مستخدم بالفعل في أمر توريد سابق له نفس الشركة.");

                dto.ExternalOrderNumber = externalOrderNumber;
            }
            else
            {
                dto.ExternalOrderNumber = null;
                dto.SupplyOrderDate = null;
            }

            var invoice = _mapper.Map<Invoice>(dto);
            invoice.Status = InvoiceStatus.Draft;
            // Temporary unique placeholder so the unique index on InvoiceNumber is satisfied.
            // Replaced with the real prefixed number (PUR-/SAL-/RET-/MAR-) when the invoice is posted.
            invoice.InvoiceNumber = $"DRAFT-{Guid.NewGuid():N}";
            invoice.SubTotal = 0;
            invoice.ItemDiscountTotal = 0;
            invoice.TotalAmount = 0;
            invoice.PostedAt = null;
            invoice.CancelledAt = null;
            invoice.CancellationReason = null;

            await _unitOfWork.InvoiceRepo.AddAsync(invoice);
            await _unitOfWork.SaveChangesAsync();
            return invoice.Id;
        }

        public async Task<bool> AddItemToDraftAsync(AddInvoiceItemDto dto)
        {
            if (dto == null) return false;

            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithItemsAsync(dto.InvoiceId);
            if (invoice == null || invoice.Status != InvoiceStatus.Draft) return false;

            var product = await _productService.GetProductByIdAsync(dto.ProductId);
            if (product == null) return false;

            if (invoice.Type == InvoiceType.MaritimeSupply &&
                product.Category != ProductCategory.FrozenFish)
            {
                throw new InvalidOperationException(
                    $"أوامر التوريد البحرية تقبل أصناف الأسماك المجمدة فقط، والصنف ({product.Name}) ليس صنفاً مجمداً.");
            }

            var unitPrice = dto.UnitPrice ?? invoice.Type switch
            {
                InvoiceType.VendorPurchase => product.PurchasePrice,
                InvoiceType.WholesaleSales => product.WholesalePrice,
                InvoiceType.StoreCashSales => product.RetailPrice,
                // Maritime supply is priced per KG.
                InvoiceType.MaritimeSupply => product.MaritimePricePerKg,
                _ => dto.UnitPrice ?? 0m
            };

            if (unitPrice is null)
            {
                throw new InvalidOperationException(
                    $"أدخل سعر الكجم للصنف ({product.Name}); لا يوجد سعر توريد افتراضي محدد له في دليل المنتجات.");
            }

            if (dto.Quantity <= 0)
                throw new InvalidOperationException("الوزن المطلوب (كجم) يجب أن يكون أكبر من صفر.");

            var item = _mapper.Map<InvoiceItem>(dto);
            item.ProductName = product.Name;
            item.UnitPrice = unitPrice.Value;
            item.LineTotal = (item.Quantity * item.UnitPrice) - item.LineDiscountAmount;

            invoice.Items.Add(item);

            RecalculateInvoiceTotals(invoice);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateDraftItemAsync(UpdateInvoiceItemDto dto)
        {
            if (dto == null) return false;

            var item = await _unitOfWork.InvoiceRepo.GetItemWithInvoiceAsync(dto.ItemId);
            if (item == null || item.Invoice.Status != InvoiceStatus.Draft) return false;

            item.Quantity = dto.Quantity;
            item.UnitPrice = dto.UnitPrice;
            item.LineDiscountAmount = dto.LineDiscountAmount;
            item.LineTotal = (item.Quantity * item.UnitPrice) - item.LineDiscountAmount;

            RecalculateInvoiceTotals(item.Invoice);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RemoveItemFromDraftAsync(int itemId)
        {
            var item = await _unitOfWork.InvoiceRepo.GetItemWithInvoiceAsync(itemId);
            if (item == null || item.Invoice.Status != InvoiceStatus.Draft) return false;

            item.Invoice.Items.Remove(item);

            RecalculateInvoiceTotals(item.Invoice);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> PostInvoiceAsync(int invoiceId)
        {
            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithItemsAsync(invoiceId);
            if (invoice == null || invoice.Status != InvoiceStatus.Draft) return false;
            if (invoice.Items == null || !invoice.Items.Any())
                throw new InvalidOperationException("لا يمكن ترحيل فاتورة بدون بنود. أضف الأصناف أولاً.");

            if (invoice.Type == InvoiceType.MaritimeSupply)
                return await PostMaritimeSupplyInvoiceAsync(invoice);

            foreach (var item in invoice.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Product?.Name))
                    item.ProductName = item.Product.Name;
            }

            RecalculateInvoiceTotals(invoice);
            invoice.InvoiceNumber = await _unitOfWork.InvoiceRepo.GetNextInvoiceNumberAsync(invoice.Type);
            invoice.Status = InvoiceStatus.Posted;
            invoice.PostedAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        private async Task<bool> PostMaritimeSupplyInvoiceAsync(Invoice invoice)
        {
            // Posting a maritime supply invoice represents the completed physical
            // dispatch: outgoing stock movements are created here, atomically with
            // the invoice posting. Revalidation inside the transaction + the process
            // gate prevents concurrent duplicate deductions.
            await StockConcurrencyGate.Gate.WaitAsync();
            try
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    if (invoice.Customer?.Type != CustomerType.MaritimeCompany)
                        throw new InvalidOperationException("لا يمكن ترحيل أمر التوريد البحري: جهة الفاتورة غير معرفة أو ليست شركة بحرية.");

                    // Group the required weight per fish item (same product may appear
                    // on multiple lines; posting validates the combined requirement).
                    var requiredByProduct = invoice.Items
                        .GroupBy(i => i.ProductId)
                        .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

                    foreach (var (productId, requiredKg) in requiredByProduct)
                    {
                        var product = await _unitOfWork.ProductRepo.GetByIdAsync(productId)
                            ?? throw new InvalidOperationException($"الصنف رقم ({productId}) غير موجود في دليل المنتجات.");

                        if (product.Category != ProductCategory.FrozenFish)
                            throw new InvalidOperationException(
                                $"الصنف ({product.Name}) ليس من أصناف الأسماك المجمدة، لا يمكن تصديره من الثلاجات.");

                        if (product.DefaultCartonWeightKg is null || product.DefaultCartonWeightKg <= 0)
                            throw new InvalidOperationException(
                                $"الصنف ({product.Name}) لا يملك وزن كرتونة محدد. حدد وزن الكرتونة في دليل المنتجات قبل الترحيل.");

                        if (requiredKg <= 0)
                            throw new InvalidOperationException($"الوزن المطلوب للصنف ({product.Name}) يجب أن يكون أكبر من صفر.");

                        // Stock is revalidated **inside** the transaction to make the
                        // read-validate-write sequence safe against concurrent posts.
                        var availableKg = await _unitOfWork.StockMovementRepo.GetAvailableWeightKgAsync(productId);
                        if (requiredKg > availableKg)
                        {
                            throw new InvalidOperationException(
                                $"لا توجد كمية كافية في الثلاجات من ({product.Name}). المطلوب: {requiredKg:N3} كجم، المتاح: {availableKg:N3} كجم.");
                        }
                    }

                    foreach (var item in invoice.Items)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Product?.Name))
                            item.ProductName = item.Product.Name;

                        var product = item.Product ?? await _unitOfWork.ProductRepo.GetByIdAsync(item.ProductId);
                        var perCarton = product!.DefaultCartonWeightKg ?? 0m;

                        var movement = new StockMovement
                        {
                            ProductId = item.ProductId,
                            ProductName = item.ProductName,
                            MovementType = StockMovementType.Out,
                            MovementDate = invoice.TransactionDate,
                            Cartons = perCarton > 0 ? -StockMovementService.Round(item.Quantity / perCarton) : 0m,
                            WeightPerCartonKg = perCarton > 0 ? StockMovementService.Round(perCarton) : null,
                            TotalWeightKg = -StockMovementService.Round(item.Quantity),
                            SourceInvoiceId = invoice.Id,
                            Reason = $"توريد مسدّد لأمر توريد ({invoice.ExternalOrderNumber ?? invoice.InvoiceNumber}) لصالح {invoice.Customer?.Name ?? "شركة بحرية"}",
                            CreatedAt = DateTime.UtcNow
                        };

                        await _unitOfWork.StockMovementRepo.AddAsync(movement);
                    }

                    RecalculateInvoiceTotals(invoice);
                    invoice.InvoiceNumber = await _unitOfWork.InvoiceRepo.GetNextInvoiceNumberAsync(invoice.Type);
                    invoice.Status = InvoiceStatus.Posted;
                    invoice.PostedAt = DateTime.UtcNow;

                    await _unitOfWork.SaveChangesAsync();
                    await _unitOfWork.CommitTransactionAsync();
                    return true;
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    throw;
                }
            }
            finally
            {
                StockConcurrencyGate.Gate.Release();
            }
        }

        public async Task<decimal> GetInvoiceRemainingBalanceAsync(int invoiceId)
        {
            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithAllocationsAndCreditNotesAsync(invoiceId);
            if (invoice == null) return 0;

            return ComputeSettlement(invoice).RemainingBalance;
        }

        public async Task<InvoicePaymentStatus> GetInvoicePaymentStatusAsync(int invoiceId)
        {
            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithAllocationsAndCreditNotesAsync(invoiceId);
            if (invoice == null) return InvoicePaymentStatus.Unpaid;

            var (_, remainingBalance) = ComputeSettlement(invoice);
            return DerivePaymentStatus(invoice.TotalAmount, remainingBalance);
        }

        public async Task<bool> CancelPostedInvoiceAsync(CancelInvoiceDto dto)
        {
            if (dto == null) return false;

            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithAllocationsAndCreditNotesAsync(dto.InvoiceId);
            if (invoice == null || invoice.Status != InvoiceStatus.Posted) return false;

            var hasActiveAllocations = invoice.PaymentAllocations.Any();
            var hasActiveCreditNotes = invoice.CreditNotes.Any(cn => cn.Status != CreditNoteStatus.Cancelled);

            if (hasActiveAllocations || hasActiveCreditNotes)
                throw new InvalidOperationException(
                    "لا يمكن إلغاء فاتورة مرتبطة بسدادات أو مرتجعات. ألغ أولاً السداد أو الإشعارات المرتبطة بنفس الحساب.");

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                invoice.Status = InvoiceStatus.Cancelled;
                invoice.CancelledAt = DateTime.UtcNow;
                invoice.CancellationReason = dto.CancellationReason;

                // Maritime supply invoices already deducted stock; undo via auditable
                // reversal movements (never hard-deleting the original ledger lines).
                if (invoice.Type == InvoiceType.MaritimeSupply)
                {
                    var movements = await _unitOfWork.StockMovementRepo.GetMovementsForInvoiceAsync(invoice.Id);
                    foreach (var movement in movements.Where(m => m.ReversalOfStockMovementId == null
                                                                  && !m.Reversals.Any()))
                    {
                        var reversal = new StockMovement
                        {
                            ProductId = movement.ProductId,
                            ProductName = movement.ProductName,
                            MovementType = movement.TotalWeightKg < 0 ? StockMovementType.In : StockMovementType.Adjustment,
                            MovementDate = DateTime.UtcNow,
                            Cartons = -movement.Cartons,
                            WeightPerCartonKg = movement.WeightPerCartonKg,
                            TotalWeightKg = -movement.TotalWeightKg,
                            SourceInvoiceId = invoice.Id,
                            ReversalOfStockMovementId = movement.Id,
                            Reason = $"عكس حركة المخزون بسبب إلغاء أمر التوريد ({invoice.InvoiceNumber}). السبب: {dto.CancellationReason}",
                            CreatedAt = DateTime.UtcNow
                        };

                        await _unitOfWork.StockMovementRepo.AddAsync(reversal);
                    }
                }

                await _unitOfWork.SaveChangesAsync();
                await _unitOfWork.CommitTransactionAsync();
                return true;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<InvoiceDto?> GetInvoiceByIdAsync(int id)
        {
            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithItemsAsync(id);
            if (invoice == null) return null;

            // Ensure all items have ProductName populated (self-heals any legacy or existing draft records)
            bool needsSave = false;
            foreach (var item in invoice.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ProductName) && item.Product != null && !string.IsNullOrWhiteSpace(item.Product.Name))
                {
                    item.ProductName = item.Product.Name;
                    needsSave = true;
                }
            }
            if (needsSave)
            {
                await _unitOfWork.SaveChangesAsync();
            }

            var dto = _mapper.Map<InvoiceDto>(invoice);

            var withSettlement = await _unitOfWork.InvoiceRepo.GetInvoiceWithAllocationsAndCreditNotesAsync(id);
            if (withSettlement != null)
            {
                var (paidAmount, remainingBalance) = ComputeSettlement(withSettlement);
                dto.PaidAmount = paidAmount;
                dto.RemainingBalance = remainingBalance;
                dto.PaymentStatus = DerivePaymentStatus(dto.TotalAmount, remainingBalance);
            }

            return dto;
        }

        public async Task<List<InvoiceDto>> GetInvoicesAsync(InvoiceType? type, InvoiceStatus? status)
        {
            var invoices = await _unitOfWork.InvoiceRepo.GetInvoicesByTypeAndStatusAsync(type, status);
            var dtos = _mapper.Map<List<InvoiceDto>>(invoices);

            foreach (var dto in dtos)
            {
                var withSettlement = await _unitOfWork.InvoiceRepo.GetInvoiceWithAllocationsAndCreditNotesAsync(dto.Id);
                if (withSettlement == null) continue;

                var (paidAmount, remainingBalance) = ComputeSettlement(withSettlement);
                dto.PaidAmount = paidAmount;
                dto.RemainingBalance = remainingBalance;
                dto.PaymentStatus = DerivePaymentStatus(dto.TotalAmount, remainingBalance);
            }

            return dtos;
        }

        private static (decimal PaidAmount, decimal RemainingBalance) ComputeSettlement(Invoice invoice)
        {
            var paidAmount = invoice.PaymentAllocations.Sum(pa => pa.Amount);
            var creditNotesTotal = invoice.CreditNotes
                .Where(cn => cn.Status == CreditNoteStatus.Posted)
                .Sum(cn => cn.TotalAmount);

            return (paidAmount, invoice.TotalAmount - paidAmount - creditNotesTotal);
        }

        private static void RecalculateInvoiceTotals(Invoice invoice)
        {
            invoice.SubTotal = invoice.Items.Sum(x => x.Quantity * x.UnitPrice);
            invoice.ItemDiscountTotal = invoice.Items.Sum(x => x.LineDiscountAmount);
            invoice.TotalAmount = invoice.SubTotal - invoice.ItemDiscountTotal - invoice.InvoiceDiscountAmount;
        }

        private static InvoicePaymentStatus DerivePaymentStatus(decimal totalAmount, decimal remainingBalance)
        {
            if (remainingBalance <= 0) return InvoicePaymentStatus.Paid;
            if (remainingBalance >= totalAmount) return InvoicePaymentStatus.Unpaid;
            return InvoicePaymentStatus.PartiallyPaid;
        }
    }
}
