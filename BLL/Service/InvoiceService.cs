using AutoMapper;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
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

            // Purchase invoices draw only on ACTIVE vendors; inactive vendors
            // remain readable through historical documents but cannot be picked
            // for new purchases.
            if (dto.VendorId.HasValue)
            {
                var vendor = await _unitOfWork.Vendors.GetByIdAsync(dto.VendorId.Value)
                    ?? throw new InvalidOperationException("المورد المحدد غير موجود في النظام.");

                if (!vendor.IsActive)
                    throw new InvalidOperationException($"المورد ({vendor.Name}) غير نشط ولا يمكن إصدار فواتير شراء له.");
            }

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
                    i.CustomerId == dto.CustomerId &&
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

            if (!product.IsActive)
                throw new InvalidOperationException($"الصنف ({product.Name}) غير نشط، لا يمكن إصدار فواتير له.");

            if (invoice.Type == InvoiceType.MaritimeSupply)
            {
                ValidateMaritimeItemSourcing(product, dto.IsFromFreezer);
            }
            else if (dto.IsFromFreezer)
            {
                throw new InvalidOperationException("خيار (التوريد من الثلاجة) مسموح فقط في أوامر التوريد للشركات البحرية.");
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

            if (dto.LineDiscountAmount < 0)
                throw new InvalidOperationException("قيمة خصم البند لا يمكن أن تكون سالبة.");

            var gross = dto.Quantity * unitPrice.Value;
            if (dto.LineDiscountAmount > gross)
                throw new InvalidOperationException($"خصم البند ({dto.LineDiscountAmount:N2} ج.م) لا يمكن أن يتجاوز إجمالي قيمة البند ({gross:N2} ج.م).");

            var item = _mapper.Map<InvoiceItem>(dto);
            item.ProductName = product.Name;
            item.UnitPrice = unitPrice.Value;
            item.LineTotal = gross - dto.LineDiscountAmount;

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

            if (dto.Quantity <= 0)
                throw new InvalidOperationException("الكمية / الوزن يجب أن يكون أكبر من صفر.");

            if (dto.UnitPrice < 0)
                throw new InvalidOperationException("سعر الوحدة لا يمكن أن يكون سالباً.");

            if (dto.LineDiscountAmount < 0)
                throw new InvalidOperationException("قيمة خصم البند لا يمكن أن تكون سالبة.");

            var gross = dto.Quantity * dto.UnitPrice;
            if (dto.LineDiscountAmount > gross)
                throw new InvalidOperationException($"خصم البند ({dto.LineDiscountAmount:N2} ج.م) لا يمكن أن يتجاوز إجمالي قيمة البند ({gross:N2} ج.م).");

            item.Quantity = dto.Quantity;
            item.UnitPrice = dto.UnitPrice;
            item.LineDiscountAmount = dto.LineDiscountAmount;
            item.LineTotal = gross - dto.LineDiscountAmount;

            // The supply source of the item can be changed while the invoice is
            // still a draft; it must follow the same per-item sourcing rules.
            var productForUpdate = await _productService.GetProductByIdAsync(item.ProductId);
            if (productForUpdate == null)
                throw new InvalidOperationException($"الصنف رقم ({item.ProductId}) غير موجود في دليل المنتجات.");

            if (!productForUpdate.IsActive)
                throw new InvalidOperationException($"الصنف ({productForUpdate.Name}) غير نشط، لا يمكن إصدار فواتير له.");

            if (item.Invoice.Type == InvoiceType.MaritimeSupply)
            {
                ValidateMaritimeItemSourcing(productForUpdate, dto.IsFromFreezer);
            }
            else if (dto.IsFromFreezer)
            {
                throw new InvalidOperationException("خيار (التوريد من الثلاجة) مسموح فقط في أوامر التوريد للشركات البحرية.");
            }
            item.IsFromFreezer = dto.IsFromFreezer;

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

        // Per-item supply source for maritime supply orders is an explicit
        // business decision made on each invoice row; it is NEVER inferred
        // from the product's frozen classification:
        //  - IsFromFreezer = true  -> the item must be a frozen-fish product
        //    (and inactive products cannot be supplied at all); freezer stock is
        //    validated and deducted only when the order is actually posted.
        //  - IsFromFreezer = false -> any active catalog product is accepted and
        //    the freezer ledger is neither inspected nor modified.
        private static void ValidateMaritimeItemSourcing(ProductDto product, bool isFromFreezer)
        {
            if (!product.IsActive)
                throw new InvalidOperationException($"الصنف ({product.Name}) غير نشط، لا يمكن إصدار فواتير له.");

            if (!isFromFreezer)
                return;

            if (product.Category != ProductCategory.FrozenFish)
                throw new InvalidOperationException(
                    $"الصنف ({product.Name}) ليس من أصناف الأسماك المجمدة، ولذلك لا يمكن تعليمه (التوريد من الثلاجة).");
        }

        // ==========================================
        // Additional charges on maritime supply orders
        // ==========================================
        private static InvoiceAdditionalCharge BuildChargeFromDto(AddInvoiceChargeDto dto)
        {
            var description = (dto.Description ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(description))
                throw new InvalidOperationException("اسم المصروف مطلوب.");
            if (dto.Description.Length > 200)
                throw new InvalidOperationException("اسم المصروف لا يمكن أن يتجاوز 200 حرف.");
            if (!string.IsNullOrWhiteSpace(dto.Notes) && dto.Notes.Length > 500)
                throw new InvalidOperationException("ملاحظات المصروف لا يمكن أن تتجاوز 500 حرف.");

            // Server-side recalculation: the Amount passed from the form is only
            // trusted when Quantity x UnitPrice cannot produce it.
            decimal amount;
            if (dto.Quantity.HasValue && dto.UnitPrice.HasValue)
            {
                if (dto.Quantity.Value <= 0)
                    throw new InvalidOperationException("كمية المصروف يجب أن تكون أكبر من صفر.");
                amount = dto.Quantity.Value * dto.UnitPrice.Value;
            }
            else
            {
                if (dto.Quantity.HasValue != dto.UnitPrice.HasValue)
                    throw new InvalidOperationException(
                        "المصروف غير مكتمل: أدخل الكمية وسعر الوحدة معاً لتُحسب القيمة تلقائياً، أو أدخل جملة القيمة المباشرة.");
                amount = dto.Amount ?? 0m;
            }

            if (amount <= 0)
                throw new InvalidOperationException("قيمة المصروف يجب أن تكون أكبر من صفر.");

            return new InvoiceAdditionalCharge
            {
                Description = description,
                Quantity = dto.Quantity,
                UnitPrice = dto.UnitPrice,
                Amount = decimal.Round(amount, 2),
                IsBillableToCompany = dto.IsBillableToCompany,
                Notes = dto.Notes
            };
        }

        public async Task<bool> AddChargeToDraftAsync(AddInvoiceChargeDto dto)
        {
            if (dto == null) return false;

            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithItemsAsync(dto.InvoiceId);
            if (invoice == null || invoice.Status != InvoiceStatus.Draft) return false;

            if (invoice.Type != InvoiceType.MaritimeSupply)
                throw new InvalidOperationException("المصاريف والتكاليف الإضافية مسموحة فقط في أوامر التوريد للشركات البحرية.");

            invoice.AdditionalCharges.Add(BuildChargeFromDto(dto));

            RecalculateInvoiceTotals(invoice);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateDraftChargeAsync(UpdateInvoiceChargeDto dto)
        {
            if (dto == null) return false;

            var charge = await _unitOfWork.InvoiceRepo.GetChargeWithInvoiceAsync(dto.ChargeId);
            if (charge == null || charge.Invoice.Status != InvoiceStatus.Draft) return false;

            if (charge.Invoice.Type != InvoiceType.MaritimeSupply)
                throw new InvalidOperationException("المصاريف والتكاليف الإضافية مسموحة فقط في أوامر التوريد للشركات البحرية.");

            var updated = BuildChargeFromDto(dto);
            charge.Description = updated.Description;
            charge.Quantity = updated.Quantity;
            charge.UnitPrice = updated.UnitPrice;
            charge.Amount = updated.Amount;
            charge.IsBillableToCompany = updated.IsBillableToCompany;
            charge.Notes = updated.Notes;

            RecalculateInvoiceTotals(charge.Invoice);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RemoveChargeFromDraftAsync(int chargeId)
        {
            var charge = await _unitOfWork.InvoiceRepo.GetChargeWithInvoiceAsync(chargeId);
            if (charge == null || charge.Invoice.Status != InvoiceStatus.Draft) return false;

            charge.Invoice.AdditionalCharges.Remove(charge);
            // cascade: the charge row is deleted when removed from the invoice collection

            RecalculateInvoiceTotals(charge.Invoice);
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

                    // Only items explicitly supplied FROM the freezer consume stock:
                    // group the required weight per fish item (same product may appear
                    // on multiple lines; posting validates the combined requirement).
                    // Non-freezer items are never inspected against the ledger.
                    var requiredByProduct = invoice.Items
                        .Where(i => i.IsFromFreezer)
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

                        // Stock-out movements are created ONLY for items whose
                        // supply source is the freezer; non-freezer items are
                        // invoiced normally and never touch the ledger.
                        if (!item.IsFromFreezer) continue;

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

            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceForAmendmentAsync(dto.InvoiceId);
            if (invoice == null || invoice.Status != InvoiceStatus.Posted) return false;

            var hasActiveCreditNotes = invoice.CreditNotes.Any(cn => cn.Status != CreditNoteStatus.Cancelled);

            if (hasActiveCreditNotes)
                throw new InvalidOperationException(
                    "لا يمكن إلغاء فاتورة مرتبطة بمرتجعات. ألغ أولاً الإشعارات المرتبطة بنفس الحساب.");

            var hasActiveAllocations = invoice.PaymentAllocations.Any();
            // ── Maritime supply orders (business-level removal of an erroneous order):
            //    - posted credit notes still block (they reverse physical returns),
            //    - payment allocations are DETACHED while their underlying CashTransaction
            //      rows are preserved: the real money remains a valid company receipt and
            //      flows as an unallocated credit/advance under the existing balance math
            //      (settled = paidTotal - allocatedToInvoices).
            // ── Other invoice types keep the strict previous behaviour.
            if (hasActiveAllocations && invoice.Type != InvoiceType.MaritimeSupply)
                throw new InvalidOperationException(
                    "لا يمكن إلغاء فاتورة مرتبطة بسدادات. ألغ أولاً السداد أو الإشعارات المرتبطة بنفس الحساب.");

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

                // Detach allocations pointing at the voided maritime order; the cash
                // transactions (real money) remain untouched and become unallocated
                // company credit/advance through the existing balance settlement math.
                foreach (var allocation in invoice.PaymentAllocations.ToList())
                {
                    invoice.PaymentAllocations.Remove(allocation);
                    _unitOfWork.PaymentAllocationRepo.Delete(allocation);
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

        // ==========================================
        // Controlled amendment of POSTED maritime supply orders
        // ==========================================
        public async Task<bool> AmendPostedMaritimeInvoiceAsync(AmendPostedMaritimeInvoiceDto dto, string? userId = null)
        {
            if (dto == null) return false;
            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new InvalidOperationException("سبب التصحيح مطلوب.");

            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceForAmendmentAsync(dto.InvoiceId);
            if (invoice == null) return false;

            if (invoice.Type != InvoiceType.MaritimeSupply)
                throw new InvalidOperationException("التصحيح المتحكم فيه متاح فقط لأوامر التوريد للشركات البحرية.");
            if (invoice.Status != InvoiceStatus.Posted)
                throw new InvalidOperationException("لا يمكن تصحيح فاتورة غير مرحلة. المسودات تُعدّل مباشرة من صفحة الفاتورة.");

            var beforeSnapshot = SnapshotInvoiceForAmendment(invoice);
            var originalItems = invoice.Items.ToList();
            var originalCharges = invoice.AdditionalCharges.ToList();

            // ===== Phase A: full validation BEFORE any entity is mutated so an
            // invalid amendment never leaves half-tracked state (InMemory-safe). =====

            if (dto.UpdatedItems.Select(x => x.ItemId).Distinct().Count() != dto.UpdatedItems.Count)
                throw new InvalidOperationException("يوجد بند مكرر في قائمة التصحيحات.");
            if (dto.RemovedItemIds.Distinct().Count() != dto.RemovedItemIds.Count)
                throw new InvalidOperationException("يوجد بند مكرر في قائمة الحذف.");
            if (dto.RemovedChargeIds.Distinct().Count() != dto.RemovedChargeIds.Count)
                throw new InvalidOperationException("يوجد مصروف مكرر في قائمة الحذف.");

            var itemsById = originalItems.ToDictionary(i => i.Id);
            foreach (var itemId in dto.RemovedItemIds)
            {
                if (!itemsById.ContainsKey(itemId))
                    throw new InvalidOperationException($"البند رقم ({itemId}) غير موجود في هذا أمر التوريد.");
                if (dto.UpdatedItems.Any(u => u.ItemId == itemId))
                    throw new InvalidOperationException($"البند رقم ({itemId}) لا يمكن أن يكون في التصحيح والحذف معاً.");
            }

            foreach (var updated in dto.UpdatedItems)
            {
                if (!itemsById.TryGetValue(updated.ItemId, out var existingItem))
                    throw new InvalidOperationException($"البند رقم ({updated.ItemId}) غير موجود في هذا أمر التوريد.");

                if (updated.Quantity <= 0)
                    throw new InvalidOperationException($"الوزن المطلوب للصنف ({existingItem.ProductName}) يجب أن يكون أكبر من صفر.");
                if (updated.UnitPrice < 0)
                    throw new InvalidOperationException($"سعر الوحدة للصنف ({existingItem.ProductName}) لا يمكن أن يكون سالباً.");
                if (updated.LineDiscountAmount < 0)
                    throw new InvalidOperationException($"خصم البند للصنف ({existingItem.ProductName}) لا يمكن أن يكون سالباً.");

                var gross = updated.Quantity * updated.UnitPrice;
                if (updated.LineDiscountAmount > gross)
                    throw new InvalidOperationException(
                        $"خصم البند ({updated.LineDiscountAmount:N2} ج.م) للصنف ({existingItem.ProductName}) لا يمكن أن يتجاوز إجمالي قيمة البند ({gross:N2} ج.م).");

                var product = await _productService.GetProductByIdAsync(existingItem.ProductId)
                    ?? throw new InvalidOperationException($"الصنف رقم ({existingItem.ProductId}) غير موجود في دليل المنتجات.");

                ValidateMaritimeItemSourcing(product, updated.IsFromFreezer);
            }

            var newItems = new List<(AddInvoiceItemDto Dto, ProductDto Product)>();
            foreach (var newDto in dto.NewItems)
            {
                var product = await _productService.GetProductByIdAsync(newDto.ProductId)
                    ?? throw new InvalidOperationException($"الصنف رقم ({newDto.ProductId}) غير موجود في دليل المنتجات.");

                if (newDto.Quantity <= 0)
                    throw new InvalidOperationException($"الوزن المطلوب للصنف ({product.Name}) يجب أن يكون أكبر من صفر.");
                if (newDto.LineDiscountAmount < 0)
                    throw new InvalidOperationException($"خصم البند للصنف ({product.Name}) لا يمكن أن يكون سالباً.");

                var unitPrice = newDto.UnitPrice ?? product.MaritimePricePerKg;
                if (unitPrice is null)
                    throw new InvalidOperationException(
                        $"أدخل سعر الكجم للصنف ({product.Name}); لا يوجد سعر توريد افتراضي محدد له في دليل المنتجات.");
                newDto.UnitPrice = unitPrice.Value;

                var gross = newDto.Quantity * unitPrice.Value;
                if (newDto.LineDiscountAmount > gross)
                    throw new InvalidOperationException(
                        $"خصم البند ({newDto.LineDiscountAmount:N2} ج.م) للصنف ({product.Name}) لا يمكن أن يتجاوز إجمالي قيمة البند ({gross:N2} ج.م).");

                ValidateMaritimeItemSourcing(product, newDto.IsFromFreezer);
                newItems.Add((newDto, product));
            }

            foreach (var chargeId in dto.RemovedChargeIds)
            {
                if (originalCharges.All(c => c.Id != chargeId))
                    throw new InvalidOperationException($"المصروف رقم ({chargeId}) غير موجود في هذا أمر التوريد.");
            }

            foreach (var chargeDto in dto.Charges)
            {
                if (chargeDto.ChargeId != 0 && originalCharges.All(c => c.Id != chargeDto.ChargeId))
                    throw new InvalidOperationException($"المصروف رقم ({chargeDto.ChargeId}) غير موجود في هذا أمر التوريد.");

                if (chargeDto.Description != null && chargeDto.Description.Length > 200)
                    throw new InvalidOperationException("اسم المصروف لا يمكن أن يتجاوز 200 حرف.");
                if (!string.IsNullOrWhiteSpace(chargeDto.Notes) && chargeDto.Notes.Length > 500)
                    throw new InvalidOperationException("ملاحظات المصروف لا يمكن أن تتجاوز 500 حرف.");
                if (chargeDto.Quantity.HasValue && chargeDto.UnitPrice.HasValue && chargeDto.Quantity.Value <= 0)
                    throw new InvalidOperationException("كمية المصروف يجب أن تكون أكبر من صفر.");
                if (chargeDto.Quantity.HasValue != chargeDto.UnitPrice.HasValue && chargeDto.Amount is null)
                    throw new InvalidOperationException(
                        "المصروف غير مكتمل: أدخل الكمية وسعر الوحدة معاً لتُحسب القيمة تلقائياً، أو أدخل جملة القيمة المباشرة.");
                if ((chargeDto.Quantity.HasValue && chargeDto.UnitPrice.HasValue
                        ? chargeDto.Quantity.Value * chargeDto.UnitPrice.Value
                        : chargeDto.Amount ?? 0m) <= 0)
                    throw new InvalidOperationException("قيمة المصروف يجب أن تكون أكبر من صفر.");
            }

            // External order number correction: same uniqueness rules as creation,
            // scoped per company and excluding cancelled orders (and this same order).
            string? correctedExternalNumber = null;
            if (dto.ExternalOrderNumber != null)
            {
                correctedExternalNumber = dto.ExternalOrderNumber.Trim();
                if (string.IsNullOrEmpty(correctedExternalNumber))
                    throw new InvalidOperationException("رقم أمر التوريد الخارجي المرسل من الشركة البحرية مطلوب.");

                var duplicated = (await _unitOfWork.InvoiceRepo.GetAllAsync()).Any(i =>
                    i.Type == InvoiceType.MaritimeSupply &&
                    i.CustomerId == invoice.CustomerId &&
                    i.Status != InvoiceStatus.Cancelled &&
                    i.Id != invoice.Id &&
                    i.ExternalOrderNumber == correctedExternalNumber);
                if (duplicated)
                    throw new InvalidOperationException($"رقم أمر التوريد ({correctedExternalNumber}) مستخدم بالفعل في أمر توريد سابق له نفس الشركة.");
            }

            // Expected new freezer requirement per product (only freezer-sourced rows).
            // The old requirement was ALREADY deducted at posting, so a post-amendment
            // increase is validated against the CURRENT available balance; a decrease
            // only forces a compensating return movement. The old requirement is
            // snapshotted BEFORE Phase B mutates the item entities in place (the
            // "original items" list holds references, not copies).
            var oldFreezerRequirementByProduct = originalItems
                .Where(i => i.IsFromFreezer)
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => StockMovementService.Round(g.Sum(i => i.Quantity)));

            var newFreezerRequirement = new Dictionary<int, decimal>();
            void TrackRequirement(int productId, decimal kg)
            {
                newFreezerRequirement[productId] = (newFreezerRequirement.TryGetValue(productId, out var current) ? current : 0m) + kg;
            }

            foreach (var updated in dto.UpdatedItems)
            {
                var existing = itemsById[updated.ItemId];
                if (updated.IsFromFreezer)
                    TrackRequirement(existing.ProductId, StockMovementService.Round(updated.Quantity));
            }
            foreach (var (newDto, _) in newItems)
            {
                if (newDto.IsFromFreezer)
                    TrackRequirement(newDto.ProductId, StockMovementService.Round(newDto.Quantity));
            }

            // An order cannot become empty: at least one item must remain.
            var remainingItemCount = originalItems.Count(i => !dto.RemovedItemIds.Contains(i.Id)) + newItems.Count;
            if (remainingItemCount == 0)
                throw new InvalidOperationException("لا يمكن أن يصبح أمر التوريد بدون بنود. ألغِ الطلب بدلاً من حذف كل بنوده.");

            // ===== Phase B: mutation inside the stock gate and the transaction,
            // mirroring the atomic posting workflow. =====
            await StockConcurrencyGate.Gate.WaitAsync();
            try
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    // Item removals
                    foreach (var itemId in dto.RemovedItemIds)
                    {
                        var removed = itemsById[itemId];
                        invoice.Items.Remove(removed);
                    }

                    // Item updates
                    foreach (var updated in dto.UpdatedItems)
                    {
                        var existing = itemsById[updated.ItemId];
                        existing.Quantity = updated.Quantity;
                        existing.UnitPrice = updated.UnitPrice;
                        existing.LineDiscountAmount = updated.LineDiscountAmount;
                        existing.LineTotal = updated.Quantity * updated.UnitPrice - updated.LineDiscountAmount;
                        existing.IsFromFreezer = updated.IsFromFreezer;
                    }

                    // Item additions
                    foreach (var (newDto, product) in newItems)
                    {
                        var item = _mapper.Map<InvoiceItem>(newDto);
                        item.InvoiceId = invoice.Id;
                        item.ProductId = product.Id;
                        item.ProductName = product.Name;
                        item.UnitPrice = newDto.UnitPrice!.Value;
                        item.LineTotal = newDto.Quantity * newDto.UnitPrice.Value - newDto.LineDiscountAmount;
                        invoice.Items.Add(item);
                    }

                    // Charge upserts and removals
                    var chargesById = originalCharges.ToDictionary(c => c.Id);
                    foreach (var chargeId in dto.RemovedChargeIds)
                    {
                        var removed = chargesById[chargeId];
                        invoice.AdditionalCharges.Remove(removed);
                    }
                    foreach (var chargeDto in dto.Charges)
                    {
                        if (chargeDto.ChargeId != 0 && chargesById.TryGetValue(chargeDto.ChargeId, out var existingCharge))
                        {
                            var updatedCharge = BuildChargeFromDto(chargeDto);
                            existingCharge.Description = updatedCharge.Description;
                            existingCharge.Quantity = updatedCharge.Quantity;
                            existingCharge.UnitPrice = updatedCharge.UnitPrice;
                            existingCharge.Amount = updatedCharge.Amount;
                            existingCharge.IsBillableToCompany = updatedCharge.IsBillableToCompany;
                            existingCharge.Notes = updatedCharge.Notes;
                        }
                        else
                        {
                            invoice.AdditionalCharges.Add(BuildChargeFromDto(chargeDto));
                        }
                    }

                    // Meta data corrections
                    if (correctedExternalNumber != null)
                        invoice.ExternalOrderNumber = correctedExternalNumber;
                    if (dto.SupplyOrderDate.HasValue)
                        invoice.SupplyOrderDate = dto.SupplyOrderDate.Value;

                    // Freezer stock reconciliation: one compensating movement per
                    // product whose net freezer requirement actually changed
                    // (price-only edits produce no movement at all). The scan runs
                    // over the UNION of old and new requirement keys, so products
                    // whose freezer rows were removed or flipped OFF entirely still
                    // produce their compensating return movement.
                    var affectedProductIds = oldFreezerRequirementByProduct.Keys
                        .Concat(newFreezerRequirement.Keys)
                        .Distinct();
                    foreach (var productId in affectedProductIds)
                    {
                        var diff = StockMovementService.Round(
                            newFreezerRequirement.GetValueOrDefault(productId) - oldFreezerRequirementByProduct.GetValueOrDefault(productId));
                        if (diff == 0)
                            continue;

                        var product = await _unitOfWork.ProductRepo.GetByIdAsync(productId)
                            ?? throw new InvalidOperationException($"الصنف رقم ({productId}) غير موجود في دليل المنتجات.");
                        var perCarton = product.DefaultCartonWeightKg ?? 0m;

                        if (diff > 0)
                        {
                            var availableKg = await _unitOfWork.StockMovementRepo.GetAvailableWeightKgAsync(productId);
                            if (diff > availableKg)
                                throw new InvalidOperationException(
                                    $"لا توجد كمية كافية في الثلاجات من ({product.Name}) لتنفيذ التصحيح. المطلوب زيادة: {diff:N3} كجم، المتاح: {availableKg:N3} كجم.");

                            await _unitOfWork.StockMovementRepo.AddAsync(new StockMovement
                            {
                                ProductId = productId,
                                ProductName = product.Name,
                                MovementType = StockMovementType.Out,
                                MovementDate = DateTime.UtcNow,
                                Cartons = perCarton > 0 ? -StockMovementService.Round(diff / perCarton) : 0m,
                                WeightPerCartonKg = perCarton > 0 ? StockMovementService.Round(perCarton) : null,
                                TotalWeightKg = -diff,
                                SourceInvoiceId = invoice.Id,
                                Reason = $"تصحيح أمر توريد ({invoice.ExternalOrderNumber ?? invoice.InvoiceNumber}) لصالح {invoice.Customer?.Name ?? "شركة بحرية"}: زيادة الوزن المسحوب من التلاجة بمقدار {diff:N3} كجم. السبب: {dto.Reason}",
                                CreatedAt = DateTime.UtcNow,
                                CreatedByUserId = userId
                            });
                        }
                        else
                        {
                            var compensation = -diff;
                            await _unitOfWork.StockMovementRepo.AddAsync(new StockMovement
                            {
                                ProductId = productId,
                                ProductName = product.Name,
                                MovementType = StockMovementType.In,
                                MovementDate = DateTime.UtcNow,
                                Cartons = perCarton > 0 ? StockMovementService.Round(compensation / perCarton) : 0m,
                                WeightPerCartonKg = perCarton > 0 ? StockMovementService.Round(perCarton) : null,
                                TotalWeightKg = compensation,
                                SourceInvoiceId = invoice.Id,
                                Reason = $"تصحيح أمر توريد ({invoice.ExternalOrderNumber ?? invoice.InvoiceNumber}) لصالح {invoice.Customer?.Name ?? "شركة بحرية"}: عوض وزن مسحوب من التلاجة بمقدار {compensation:N3} كجم. السبب: {dto.Reason}",
                                CreatedAt = DateTime.UtcNow,
                                CreatedByUserId = userId
                            });
                        }
                    }

                    RecalculateInvoiceTotals(invoice);

                    invoice.Amendments.Add(new InvoiceAmendment
                    {
                        InvoiceId = invoice.Id,
                        AmendedAtUtc = DateTime.UtcNow,
                        AmendedByUserId = userId,
                        Reason = dto.Reason.Trim(),
                        BeforeJson = beforeSnapshot,
                        AfterJson = SnapshotInvoiceForAmendment(invoice),
                        CreatedAt = DateTime.UtcNow
                    });

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

        // Serialized before/after financial + stock-relevant snapshot for the audit
        // trail written to InvoiceAmendment.
        private static string SnapshotInvoiceForAmendment(Invoice invoice)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                invoice.InvoiceNumber,
                invoice.ExternalOrderNumber,
                invoice.SupplyOrderDate,
                invoice.SubTotal,
                invoice.ItemDiscountTotal,
                invoice.InvoiceDiscountAmount,
                invoice.TotalAmount,
                BillableChargesTotal = invoice.AdditionalCharges?.Where(c => c.IsBillableToCompany).Sum(c => c.Amount) ?? 0m,
                InternalChargesTotal = invoice.AdditionalCharges?.Where(c => !c.IsBillableToCompany).Sum(c => c.Amount) ?? 0m,
                AllocatedPayments = invoice.PaymentAllocations?.Sum(pa => pa.Amount) ?? 0m,
                Items = invoice.Items.Select(i => new
                {
                    i.Id, i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.LineDiscountAmount, i.IsFromFreezer
                }),
                Charges = (invoice.AdditionalCharges ?? new List<InvoiceAdditionalCharge>()).Select(c => new
                {
                    c.Id, c.Description, c.Quantity, c.UnitPrice, c.Amount, c.IsBillableToCompany
                })
            });
        }

        // ==========================================
        // Safe deletion of DRAFT supply orders
        // ==========================================
        public async Task<bool> DeleteDraftInvoiceAsync(int invoiceId)
        {
            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceForAmendmentAsync(invoiceId);
            if (invoice == null || invoice.Status != InvoiceStatus.Draft) return false;
            if (invoice.StockMovements.Count > 0 || invoice.PaymentAllocations.Count > 0 || invoice.CreditNotes.Count > 0)
                throw new InvalidOperationException("لا يمكن حذف فاتورة مسودة مرتبطة سابقاً بمخزون أو سدادات. ألغِ أو راجع السداد أولاً.");

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                // Draft rows never touched stock or balances: a true physical delete
                // of the draft and its draft-only children (no orphaned records).
                foreach (var item in invoice.Items.ToList())
                    invoice.Items.Remove(item);
                foreach (var charge in invoice.AdditionalCharges.ToList())
                    invoice.AdditionalCharges.Remove(charge);
                _unitOfWork.InvoiceRepo.Delete(invoice);

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

            dto.BillableChargesTotal = dto.AdditionalCharges.Where(c => c.IsBillableToCompany).Sum(c => c.Amount);
            dto.InternalChargesTotal = dto.AdditionalCharges.Where(c => !c.IsBillableToCompany).Sum(c => c.Amount);
            dto.Amendments = dto.Amendments
                .OrderByDescending(a => a.AmendedAtUtc)
                .ToList();

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
                dto.BillableChargesTotal = dto.AdditionalCharges.Where(c => c.IsBillableToCompany).Sum(c => c.Amount);
                dto.InternalChargesTotal = dto.AdditionalCharges.Where(c => !c.IsBillableToCompany).Sum(c => c.Amount);
            }

            return dtos;
        }

        // Posted maritime supply orders (with their items) for the maritime
        // company account statements. Both statement modes (summary + detailed)
        // consume this exact query so their financial totals always agree.
        // Date window semantics match StatementBuilder: [from, to] inclusive days.
        public async Task<List<MaritimeSupplyOrderDto>> GetMaritimeSupplyOrdersAsync(int? customerId, DateTime? from, DateTime? to)
        {
            var invoices = await _unitOfWork.InvoiceRepo.GetPostedMaritimeSupplyOrdersAsync(customerId)
                           ?? new List<Invoice>();

            var fromDate = from?.Date;
            var toDate = to?.Date;

            var orders = new List<MaritimeSupplyOrderDto>();
            foreach (var invoice in invoices)
            {
                if (fromDate.HasValue && invoice.TransactionDate.Date < fromDate.Value) continue;
                if (toDate.HasValue && invoice.TransactionDate.Date >= toDate.Value.AddDays(1)) continue;

                orders.Add(new MaritimeSupplyOrderDto
                {
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    ExternalOrderNumber = invoice.ExternalOrderNumber ?? string.Empty,
                    SupplyOrderDate = invoice.SupplyOrderDate,
                    TransactionDate = invoice.TransactionDate,
                    CustomerId = invoice.CustomerId ?? 0,
                    CompanyName = invoice.Customer?.Name ?? string.Empty,
                    TotalAmount = invoice.TotalAmount,
                    Items = invoice.Items
                        .OrderBy(i => i.Id)
                        .Select(i => new MaritimeSupplyOrderItemDto
                        {
                            ProductId = i.ProductId,
                            ProductName = !string.IsNullOrWhiteSpace(i.ProductName)
                                ? i.ProductName
                                : (i.Product != null ? i.Product.Name : "صنف غير محدد"),
                            Quantity = i.Quantity,
                            UnitPrice = i.UnitPrice,
                            LineDiscountAmount = i.LineDiscountAmount,
                            LineTotal = i.LineTotal,
                            IsFromFreezer = i.IsFromFreezer
                        })
                        .ToList(),
                    Charges = invoice.AdditionalCharges
                        .OrderBy(c => c.Id)
                        .Select(c => new InvoiceChargeDto
                        {
                            Id = c.Id,
                            InvoiceId = c.InvoiceId,
                            Description = c.Description,
                            Quantity = c.Quantity,
                            UnitPrice = c.UnitPrice,
                            Amount = c.Amount,
                            IsBillableToCompany = c.IsBillableToCompany,
                            Notes = c.Notes
                        })
                        .ToList()
                });

                var last = orders[^1];
                last.BillableChargesTotal = last.Charges.Where(c => c.IsBillableToCompany).Sum(c => c.Amount);
                last.InternalChargesTotal = last.Charges.Where(c => !c.IsBillableToCompany).Sum(c => c.Amount);
            }

            return orders;
        }

        private static (decimal PaidAmount, decimal RemainingBalance) ComputeSettlement(Invoice invoice)
        {
            var paidAmount = invoice.PaymentAllocations.Sum(pa => pa.Amount);
            var creditNotesTotal = invoice.CreditNotes
                .Where(cn => cn.Status == CreditNoteStatus.Posted)
                .Sum(cn => cn.TotalAmount);

            return (paidAmount, invoice.TotalAmount - paidAmount - creditNotesTotal);
        }

        // Final supply order amount = products subtotal
        //   - item discounts - invoice discount
        //   + billable additional charges
        // Internal-only charges are tracked for cost but never billed.
        private static void RecalculateInvoiceTotals(Invoice invoice)
        {
            invoice.SubTotal = invoice.Items.Sum(x => x.Quantity * x.UnitPrice);
            invoice.ItemDiscountTotal = invoice.Items.Sum(x => x.LineDiscountAmount);
            var billableCharges = invoice.AdditionalCharges == null
                ? 0m
                : invoice.AdditionalCharges.Where(c => c.IsBillableToCompany).Sum(c => c.Amount);
            invoice.TotalAmount = invoice.SubTotal - invoice.ItemDiscountTotal - invoice.InvoiceDiscountAmount + billableCharges;
        }

        private static InvoicePaymentStatus DerivePaymentStatus(decimal totalAmount, decimal remainingBalance)
        {
            if (remainingBalance <= 0) return InvoicePaymentStatus.Paid;
            if (remainingBalance >= totalAmount) return InvoicePaymentStatus.Unpaid;
            return InvoicePaymentStatus.PartiallyPaid;
        }
    }
}
