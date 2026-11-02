using AutoMapper;
using BLL.DTOs.Payment.InComingData;
using BLL.DTOs.Payment.OutGoingData;
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
    public class PaymentService : IPaymentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IInvoiceService _invoiceService;

        /// <summary>Synthetic row id used for the opening-balance line in the payment allocation table.</summary>
        public const int OpeningBalanceRowId = 0;
        public const string OpeningBalanceRowLabel = "رصيد افتتاحي سابق";

        public PaymentService(IUnitOfWork unitOfWork, IMapper mapper, IInvoiceService invoiceService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _invoiceService = invoiceService;
        }

        public async Task<int> RecordCustomerPaymentAsync(CreateCustomerPaymentDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto), "بيانات الدفعة غير صحيحة.");

            if (dto.Amount <= 0)
                throw new InvalidOperationException("المبلغ المدفوع يجب أن يكون أكبر من صفر.");

            if (dto.Allocations == null || !dto.Allocations.Any())
                throw new InvalidOperationException("يجب اختيار فاتورة واحدة على الأقل لتوزيع المبلغ عليها.");

            if (dto.Allocations.Select(i => i.InvoiceId).Distinct().Count() != dto.Allocations.Count)
                throw new InvalidOperationException("تم تكرار إحدى الفواتير في قائمة التوزيع.");

            var totalAllocated = dto.Allocations.Sum(t => t.AllocatedAmount);
            if (dto.Amount != totalAllocated)
            {
                throw new InvalidOperationException(
                    $"إجمالي مبالغ التوزيع ({totalAllocated:N2}) يجب أن يساوي تماماً المبلغ المدفوع ({dto.Amount:N2}).");
            }

            var customer = await _unitOfWork.CustomerRepo.GetByIdAsync(dto.CustomerId);
            if (customer == null || !customer.IsActive)
                throw new InvalidOperationException("العميل غير موجود أو حسابه معطل.");

            // Invoice-vs-party type compatibility: wholesale payments settle
            // wholesale invoices; maritime company payments settle supply invoices.
            // Crossovers would silently mix two independent account worlds.
            var expectedInvoiceType = customer.Type == CustomerType.MaritimeCompany
                ? InvoiceType.MaritimeSupply
                : InvoiceType.WholesaleSales;

            var customerTotalBalance = await _unitOfWork.CustomerRepo.GetCustomerBalanceAsync(dto.CustomerId);
            if (dto.Amount > customerTotalBalance)
            {
                throw new InvalidOperationException(
                    $"المبلغ المدفوع ({dto.Amount:N2}) يتجاوز إجمالي مديونية العميل ({customerTotalBalance:N2}). لا يُسمح بالدفع الزائد.");
            }

            foreach (var allocation in dto.Allocations)
            {
                if (allocation.AllocatedAmount <= 0)
                    throw new InvalidOperationException("مبلغ التوزيع لكل فاتورة يجب أن يكون أكبر من صفر.");

                // InvoiceId == 0 => settlement of the customer's opening balance (رصيد افتتاحي).
                if (allocation.InvoiceId == OpeningBalanceRowId)
                {
                    var (_, openingRemaining, _) = await _unitOfWork.CustomerRepo.GetOpeningBalanceSettlementAsync(dto.CustomerId);
                    if (openingRemaining <= 0)
                        throw new InvalidOperationException("لا يوجد رصيد افتتاحي مستحق على هذا العميل.");
                    if (allocation.AllocatedAmount > openingRemaining)
                    {
                        throw new InvalidOperationException(
                            $"مبلغ التوزيع ({allocation.AllocatedAmount:N2}) أكبر من المتبقي من الرصيد الافتتاحي ({openingRemaining:N2}).");
                    }
                    continue;
                }

                var invoice = await _unitOfWork.InvoiceRepo.GetByIdAsync(allocation.InvoiceId);
                if (invoice == null)
                    throw new InvalidOperationException($"الفاتورة رقم {allocation.InvoiceId} غير موجودة.");

                if (invoice.CustomerId != dto.CustomerId)
                    throw new InvalidOperationException($"الفاتورة {invoice.InvoiceNumber} لا تنتمي لهذا العميل.");

                if (invoice.Type != expectedInvoiceType)
                {
                    throw new InvalidOperationException(customer.Type == CustomerType.MaritimeCompany
                        ? $"الفاتورة ({invoice.InvoiceNumber}) ليست أمر توريد بحري، ولا يجوز سداده من حساب شركة بحرية."
                        : $"الفاتورة ({invoice.InvoiceNumber}) فاتورة توريد لشركة بحرية، ولا يجوز سداده من حساب عميل جملة.");
                }

                if (invoice.Status != InvoiceStatus.Posted)
                    throw new InvalidOperationException($"لا يمكن السداد لفاتورة غير مرحلة ({invoice.InvoiceNumber}).");

                var invoiceRemaining = await _invoiceService.GetInvoiceRemainingBalanceAsync(allocation.InvoiceId);
                if (allocation.AllocatedAmount > invoiceRemaining)
                {
                    throw new InvalidOperationException(
                        $"مبلغ التوزيع ({allocation.AllocatedAmount:N2}) أكبر من المتبقي على الفاتورة {invoice.InvoiceNumber} ({invoiceRemaining:N2}).");
                }
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var cashTx = new CashTransaction
                {
                    TransactionDate = dto.PaymentDate,
                    Amount = dto.Amount,
                    Direction = CashDirection.In,
                    Category = CashCategory.CustomerPayment,
                    CustomerId = dto.CustomerId,
                    Notes = dto.Notes,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.CashTransactionRepo.AddAsync(cashTx);
                await _unitOfWork.SaveChangesAsync();

                var allocations = dto.Allocations
                    .Where(a => a.InvoiceId != OpeningBalanceRowId)
                    .Select(a => new PaymentAllocation
                    {
                        CashTransactionId = cashTx.Id,
                        InvoiceId = a.InvoiceId,
                        Amount = a.AllocatedAmount,
                        CreatedAt = DateTime.UtcNow
                    }).ToList();

                if (allocations.Any())
                {
                    await _unitOfWork.PaymentAllocationRepo.AddRangeAsync(allocations);
                    await _unitOfWork.SaveChangesAsync();
                }

                await _unitOfWork.CommitTransactionAsync();
                return cashTx.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<int> RecordVendorPaymentAsync(CreateVendorPaymentDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto), "بيانات الدفعة غير صحيحة.");

            if (dto.Amount <= 0)
                throw new InvalidOperationException("المبلغ المدفوع يجب أن يكون أكبر من صفر.");

            if (dto.Allocations == null || !dto.Allocations.Any())
                throw new InvalidOperationException("يجب اختيار فاتورة واحدة على الأقل لتوزيع المبلغ عليها.");

            if (dto.Allocations.Select(i => i.InvoiceId).Distinct().Count() != dto.Allocations.Count)
                throw new InvalidOperationException("تم تكرار إحدى الفواتير في قائمة التوزيع.");

            var totalAllocated = dto.Allocations.Sum(t => t.AllocatedAmount);
            if (dto.Amount != totalAllocated)
            {
                throw new InvalidOperationException(
                    $"إجمالي مبالغ التوزيع ({totalAllocated:N2}) يجب أن يساوي تماماً المبلغ المدفوع ({dto.Amount:N2}).");
            }

            var customer = await _unitOfWork.Vendors.GetByIdAsync(dto.VendorId);
            if (customer == null || !customer.IsActive)
                throw new InvalidOperationException("المورد غير موجود أو حسابه معطل.");

            var customerTotalBalance = await _unitOfWork.Vendors.GetVendorBalanceAsync(dto.VendorId);
            if (dto.Amount > customerTotalBalance)
            {
                throw new InvalidOperationException(
                    $"المبلغ المدفوع ({dto.Amount:N2}) يتجاوز إجمالي مستحقات المورد ({customerTotalBalance:N2}). لا يُسمح بالدفع الزائد.");
            }

            foreach (var allocation in dto.Allocations)
            {
                if (allocation.AllocatedAmount <= 0)
                    throw new InvalidOperationException("مبلغ التوزيع لكل فاتورة يجب أن يكون أكبر من صفر.");

                // InvoiceId == 0 => settlement of the vendor's opening balance (رصيد افتتاحي).
                if (allocation.InvoiceId == OpeningBalanceRowId)
                {
                    var (_, openingRemaining, _) = await _unitOfWork.Vendors.GetOpeningBalanceSettlementAsync(dto.VendorId);
                    if (openingRemaining <= 0)
                        throw new InvalidOperationException("لا يوجد رصيد افتتاحي مستحق لهذا المورد.");
                    if (allocation.AllocatedAmount > openingRemaining)
                    {
                        throw new InvalidOperationException(
                            $"مبلغ التوزيع ({allocation.AllocatedAmount:N2}) أكبر من المتبقي من الرصيد الافتتاحي ({openingRemaining:N2}).");
                    }
                    continue;
                }

                var invoice = await _unitOfWork.InvoiceRepo.GetByIdAsync(allocation.InvoiceId);
                if (invoice == null)
                    throw new InvalidOperationException($"الفاتورة رقم {allocation.InvoiceId} غير موجودة.");

                if (invoice.VendorId != dto.VendorId)
                    throw new InvalidOperationException($"الفاتورة {invoice.InvoiceNumber} لا تنتمي لهذاالمورد.");

                if (invoice.Status != InvoiceStatus.Posted)
                    throw new InvalidOperationException($"لا يمكن السداد لفاتورة غير مرحلة ({invoice.InvoiceNumber}).");

                var invoiceRemaining = await _invoiceService.GetInvoiceRemainingBalanceAsync(allocation.InvoiceId);
                if (allocation.AllocatedAmount > invoiceRemaining)
                {
                    throw new InvalidOperationException(
                        $"مبلغ التوزيع ({allocation.AllocatedAmount:N2}) أكبر من المتبقي على الفاتورة {invoice.InvoiceNumber} ({invoiceRemaining:N2}).");
                }
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                var cashTx = new CashTransaction
                {
                    TransactionDate = dto.PaymentDate,
                    Amount = dto.Amount,
                    Direction = CashDirection.Out,
                    Category = CashCategory.VendorPayment,
                    VendorId = dto.VendorId,
                    Notes = dto.Notes,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.CashTransactionRepo.AddAsync(cashTx);
                await _unitOfWork.SaveChangesAsync();

                var allocations = dto.Allocations
                    .Where(a => a.InvoiceId != OpeningBalanceRowId)
                    .Select(a => new PaymentAllocation
                    {
                        CashTransactionId = cashTx.Id,
                        InvoiceId = a.InvoiceId,
                        Amount = a.AllocatedAmount,
                        CreatedAt = DateTime.UtcNow
                    }).ToList();

                if (allocations.Any())
                {
                    await _unitOfWork.PaymentAllocationRepo.AddRangeAsync(allocations);
                    await _unitOfWork.SaveChangesAsync();
                }

                await _unitOfWork.CommitTransactionAsync();
                return cashTx.Id;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<List<OpenInvoiceForPaymentDto>> GetCustomerOpenInvoicesAsync(int customerId)
        {
            var resultOpenInvoicesList = new List<OpenInvoiceForPaymentDto>();

            var (openingAmount, openingRemaining, effectiveDate) = await _unitOfWork.CustomerRepo.GetOpeningBalanceSettlementAsync(customerId);
            if (openingRemaining > 0)
            {
                resultOpenInvoicesList.Add(new OpenInvoiceForPaymentDto
                {
                    InvoiceId = OpeningBalanceRowId,
                    InvoiceNumber = OpeningBalanceRowLabel,
                    TransactionDate = effectiveDate ?? DateTime.Today,
                    TotalAmount = openingAmount,
                    PaidAmount = openingAmount - openingRemaining,
                    RemainingBalance = openingRemaining,
                    PaymentStatus = openingRemaining < openingAmount ? InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid
                });
            }

            var openInovices = await _unitOfWork.InvoiceRepo.GetCustomerOpenInvoices(customerId);
            foreach (var openInvoice in openInovices)
            {
                var remaining = await _invoiceService.GetInvoiceRemainingBalanceAsync(openInvoice.Id);
                if (remaining > 0)
                {
                    var mappedInvoices = _mapper.Map<OpenInvoiceForPaymentDto>(openInvoice);
                    mappedInvoices.InvoiceId = openInvoice.Id;
                    mappedInvoices.RemainingBalance = remaining;
                    mappedInvoices.PaidAmount = openInvoice.TotalAmount - remaining;
                    mappedInvoices.PaymentStatus = mappedInvoices.PaidAmount > 0 ?
                        InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid;
                    resultOpenInvoicesList.Add(mappedInvoices);
                }
            }
            return resultOpenInvoicesList;
        }

        public async Task<List<OpenInvoiceForPaymentDto>> GetVendorOpenInvoicesAsync(int vendorId)
        {
            var resultOpenInvoicesList = new List<OpenInvoiceForPaymentDto>();

            var (openingAmount, openingRemaining, effectiveDate) = await _unitOfWork.Vendors.GetOpeningBalanceSettlementAsync(vendorId);
            if (openingRemaining > 0)
            {
                resultOpenInvoicesList.Add(new OpenInvoiceForPaymentDto
                {
                    InvoiceId = OpeningBalanceRowId,
                    InvoiceNumber = OpeningBalanceRowLabel,
                    TransactionDate = effectiveDate ?? DateTime.Today,
                    TotalAmount = openingAmount,
                    PaidAmount = openingAmount - openingRemaining,
                    RemainingBalance = openingRemaining,
                    PaymentStatus = openingRemaining < openingAmount ? InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid
                });
            }

            var openInovices = await _unitOfWork.InvoiceRepo.GetVendorOpenInvoices(vendorId);
            foreach (var openInvoice in openInovices)
            {
                var remaining = await _invoiceService.GetInvoiceRemainingBalanceAsync(openInvoice.Id);
                if (remaining > 0)
                {
                    var mappedInvoices = _mapper.Map<OpenInvoiceForPaymentDto>(openInvoice);
                    mappedInvoices.InvoiceId = openInvoice.Id;
                    mappedInvoices.RemainingBalance = remaining;
                    mappedInvoices.PaidAmount = openInvoice.TotalAmount - remaining;
                    mappedInvoices.PaymentStatus = mappedInvoices.PaidAmount > 0 ?
                        InvoicePaymentStatus.PartiallyPaid : InvoicePaymentStatus.Unpaid;
                    resultOpenInvoicesList.Add(mappedInvoices);
                }
            }
            return resultOpenInvoicesList;
        }
    }
}
