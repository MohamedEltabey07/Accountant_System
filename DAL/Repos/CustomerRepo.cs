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
    public class CustomerRepo : GenericRepo<Customer>, ICustomerRepo
    {
        private readonly AppDbcontext _context;
        public CustomerRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<List<Customer>> GetActiveCustomersAsync()
        {
            var listOfActiveCustomers = await _context.Customers.Where(a => a.IsActive).ToListAsync();
            return listOfActiveCustomers;
        }

        public async Task<decimal> GetCustomerBalanceAsync(int customerId)
        {
            // 1. Make sure the customer exists.
            var customerExists = await _context.Customers
                .AnyAsync(c => c.Id == customerId);

            if (!customerExists)
            {
                throw new KeyNotFoundException(
                    $"Customer with ID {customerId} was not found.");
            }

            // 2. Get the customer's opening debt.
            var openingBalance = await _context.OpeningBalanceEntries
                .Where(x =>
                    x.CustomerId == customerId &&
                    x.VendorId == null)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;

            // 3. Get posted wholesale sales only.
            var sales = await _context.Invoices
                .Where(i =>
                    i.CustomerId == customerId &&
                    i.Type == InvoiceType.WholesaleSales &&
                    i.Status == InvoiceStatus.Posted)
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0m;

     
            var creditNotes = await _context.CreditNotes
                .Where(cn =>
                    cn.Type == CreditNoteType.CustomerReturn &&
                    cn.Status == CreditNoteStatus.Posted &&
                    cn.OriginalInvoice.CustomerId == customerId &&
                    cn.OriginalInvoice.Type == InvoiceType.WholesaleSales &&
                    cn.OriginalInvoice.Status == InvoiceStatus.Posted)
                .SumAsync(cn => (decimal?)cn.TotalAmount) ?? 0m;

           
            var payments = await _context.CashTransactions
                .Where(t =>
                    t.CustomerId == customerId &&
                    t.Direction == CashDirection.In &&
                    t.Category == CashCategory.CustomerPayment &&
                    t.ReversalOfTransactionId == null &&
                    !_context.CashTransactions.Any(r =>
                        r.ReversalOfTransactionId == t.Id))
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            // 6. Get valid cash refunds paid back to the customer.
            var refunds = await _context.CashTransactions
                .Where(t =>
                    t.CustomerId == customerId &&
                    t.Direction == CashDirection.Out &&
                    t.Category == CashCategory.CustomerRefund &&
                    t.ReversalOfTransactionId == null &&
                    !_context.CashTransactions.Any(r =>
                        r.ReversalOfTransactionId == t.Id))
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            // 7. Calculate the customer's final balance.
            return openingBalance
                   + sales
                   - creditNotes
                   - payments
                   + refunds;
        }

        public async Task<Customer?> GetCustomerWithFinancialDetailsAsync(int customerId)
        {
            var customerFinancialDetail = await _context.Customers.Where(cid => cid.Id == customerId).Include(i => i.Invoices).
                Include(c => c.CashTransactions).Include(o => o.OpeningBalanceEntries).FirstOrDefaultAsync();
            if (customerFinancialDetail == null) return null;
            return customerFinancialDetail;
        }

        public async Task<bool> HasFinancialHistoryAsync(int customerId)
        {
            var checkCustomerInvoice = await _context.Invoices.Where(i => i.CustomerId == customerId).FirstOrDefaultAsync();
            var checkCustomerOpeningBalance = await _context.OpeningBalanceEntries.Where(i => i.CustomerId == customerId).FirstOrDefaultAsync();
            if (checkCustomerOpeningBalance == null && checkCustomerInvoice == null) return false;
            return true;
        }
    }
}
