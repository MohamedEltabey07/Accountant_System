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
                _ => false
            };
            if (!isValidType) return 0;

            var invoice = _mapper.Map<Invoice>(dto);
            invoice.Status = InvoiceStatus.Draft;
            // Temporary unique placeholder so the unique index on InvoiceNumber is satisfied.
            // Replaced with the real prefixed number (PUR-/SAL-/RET-) when the invoice is posted.
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

            var unitPrice = dto.UnitPrice ?? invoice.Type switch
            {
                InvoiceType.VendorPurchase => product.PurchasePrice,
                InvoiceType.WholesaleSales => product.WholesalePrice,
                InvoiceType.StoreCashSales => product.RetailPrice,
                _ => dto.UnitPrice ?? 0m
            };

            var item = _mapper.Map<InvoiceItem>(dto);
            item.ProductName = product.Name;
            item.UnitPrice = unitPrice;
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
            if (invoice.Items == null || !invoice.Items.Any()) return false;

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
            if (hasActiveAllocations || hasActiveCreditNotes) return false;

            invoice.Status = InvoiceStatus.Cancelled;
            invoice.CancelledAt = DateTime.UtcNow;
            invoice.CancellationReason = dto.CancellationReason;

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<InvoiceDto?> GetInvoiceByIdAsync(int id)
        {
            var invoice = await _unitOfWork.InvoiceRepo.GetInvoiceWithItemsAsync(id);
            if (invoice == null) return null;

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
