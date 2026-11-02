using DAL.AppDbContext;
using DAL.Entities;
using DAL.Enums;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.Repos
{
    public class VendorRepo : GenericRepo<Vendor>, IVendorRepo
    {
        private readonly AppDbcontext _context;
        public VendorRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }
        public async Task<List<Vendor>> GetActiveVendorAsync()
        {
            var listOfActiveCustomers = await _context.Vendors.Where(a => a.IsActive).ToListAsync();
            return listOfActiveCustomers;
        }

        public async Task<decimal> GetVendorBalanceAsync(int vendorId)
        {
            var vendorExists = await _context.Vendors
                .AnyAsync(v => v.Id == vendorId);

            if (!vendorExists)
            {
                throw new KeyNotFoundException(
                    $"Vendor with ID {vendorId} was not found.");
            }

            var openingBalance = await _context.OpeningBalanceEntries
                .Where(x =>
                    x.VendorId == vendorId &&
                    x.CustomerId == null)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;

            var purchases = await _context.Invoices
                .Where(i =>
                    i.VendorId == vendorId &&
                    i.Type == InvoiceType.VendorPurchase &&
                    i.Status == InvoiceStatus.Posted)
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0m;

            var creditNotes = await _context.CreditNotes
                .Where(cn =>
                    cn.Type == CreditNoteType.VendorReturn &&
                    cn.Status == CreditNoteStatus.Posted &&
                    cn.OriginalInvoice.VendorId == vendorId &&
                    cn.OriginalInvoice.Type == InvoiceType.VendorPurchase &&
                    cn.OriginalInvoice.Status == InvoiceStatus.Posted)
                .SumAsync(cn => (decimal?)cn.TotalAmount) ?? 0m;

            var payments = await _context.CashTransactions
                .Where(t =>
                    t.VendorId == vendorId &&
                    t.Direction == CashDirection.Out &&
                    t.Category == CashCategory.VendorPayment &&
                    t.ReversalOfTransactionId == null &&
                    !_context.CashTransactions.Any(r =>
                        r.ReversalOfTransactionId == t.Id))
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            var refunds = await _context.CashTransactions
                .Where(t =>
                    t.VendorId == vendorId &&
                    t.Direction == CashDirection.In &&
                    t.Category == CashCategory.VendorRefund &&
                    t.ReversalOfTransactionId == null &&
                    !_context.CashTransactions.Any(r =>
                        r.ReversalOfTransactionId == t.Id))
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            return openingBalance
                   + purchases
                   - payments
                   - creditNotes
                   + refunds;
        }

        /// <summary>
        /// Unallocated portion of valid vendor payments = opening balance settlement
        /// (overpayment is blocked, so no other destination exists).
        /// </summary>
        public async Task<(decimal OpeningAmount, decimal Remaining, DateTime? EffectiveDate)> GetOpeningBalanceSettlementAsync(int vendorId)
        {
            var opening = await _context.OpeningBalanceEntries
                .Where(x => x.VendorId == vendorId && x.CustomerId == null)
                .AsNoTracking()
                .ToListAsync();

            var openingAmount = opening.Sum(x => x.Amount);
            if (openingAmount <= 0) return (0m, 0m, null);

            var validPayments = _context.CashTransactions
                .Where(t =>
                    t.VendorId == vendorId &&
                    t.Direction == CashDirection.Out &&
                    t.Category == CashCategory.VendorPayment &&
                    t.ReversalOfTransactionId == null &&
                    !_context.CashTransactions.Any(r => r.ReversalOfTransactionId == t.Id));

            var paidTotal = await validPayments.SumAsync(t => (decimal?)t.Amount) ?? 0m;
            var allocatedToInvoices = await _context.PaymentAllocations
                .Where(pa => validPayments.Any(t => t.Id == pa.CashTransactionId))
                .SumAsync(pa => (decimal?)pa.Amount) ?? 0m;

            var settled = Math.Max(0m, paidTotal - allocatedToInvoices);
            var remaining = Math.Max(0m, openingAmount - settled);
            return (openingAmount, remaining, opening.Min(x => x.EffectiveDate));
        }

        public async Task<Vendor?> GetVendorWithFinancialDetailsAsync(int vendorId)
        {
            var vendorFinancialDetail = await _context.Vendors.Where(cid => cid.Id == vendorId).Include(i => i.Invoices).
                Include(c => c.CashTransactions).Include(o => o.OpeningBalanceEntries).FirstOrDefaultAsync();
            if (vendorFinancialDetail == null) return null;
            return vendorFinancialDetail;
        }

        public async Task<bool> HasFinancialHistoryAsync(int vendorId)
        {
            var checkVendorInvoice = await _context.Invoices.Where(i => i.VendorId == vendorId).FirstOrDefaultAsync();
            var checkVendorOpeningBalance = await _context.OpeningBalanceEntries.Where(i => i.VendorId == vendorId).FirstOrDefaultAsync();
            if (checkVendorOpeningBalance == null && checkVendorInvoice == null) return false;
            return true;
        }
    }
}
