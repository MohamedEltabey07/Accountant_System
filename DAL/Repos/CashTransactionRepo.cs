using DAL.AppDbContext;
using DAL.Entities;
using DAL.Enums;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repos
{
    public class CashTransactionRepo : GenericRepo<CashTransaction>, ICashTransactionRepo
    {
        private readonly AppDbcontext _context;

        public CashTransactionRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<CashTransaction?> GetByIdWithDetailsAsync(int id)
        {
            var cashTrans = await _context.CashTransactions.Where(i => i.Id == id)
                .Include(c => c.Customer).Include(v => v.Vendor)
                .Include(e => e.ExpenseCategory).Include(s => s.SourceInvoice).Include(p => p.PaymentAllocations).ThenInclude(i => i.Invoice).AsNoTracking()
                .FirstOrDefaultAsync();
            if (cashTrans == null) return null;
            return cashTrans;
        }

        public async Task<List<CashTransaction>> GetFilteredTransactionsAsync(DateTime? fromDate, DateTime? toDate, CashCategory? category, CashDirection? direction)
        {
            var query = _context.CashTransactions
                .Include(c => c.Customer)
                .Include(v => v.Vendor)
                .Include(e => e.ExpenseCategory)
                .Include(s => s.SourceInvoice)
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(i => i.TransactionDate >= fromDate.Value.Date);

            if (toDate.HasValue)
            {
                var toDateExclusive = toDate.Value.Date.AddDays(1);
                query = query.Where(i => i.TransactionDate < toDateExclusive);
            }

            if (category.HasValue)
                query = query.Where(i => i.Category == category.Value);

            if (direction.HasValue)
                query = query.Where(i => i.Direction == direction.Value);

            return await query
                .OrderByDescending(i => i.TransactionDate)
                .ThenByDescending(i => i.Id)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<CashTransaction>> GetCustomerPaymentsAsync(int customerId)
        {
            var customerPayment = await _context.CashTransactions.Where(c => c.CustomerId == customerId
            && c.VendorId == null && c.Category == CashCategory.CustomerPayment)
            .OrderByDescending(t => t.TransactionDate)
            .AsNoTracking().ToListAsync();
            if (!customerPayment.Any()) return null;
            return customerPayment;
        }

        public async Task<List<CashTransaction>> GetVendorPaymentsAsync(int vendorId)
        {
            var vendorPayment = await _context.CashTransactions
                .Where(c => c.VendorId == vendorId && c.Category == CashCategory.VendorPayment)
                .OrderByDescending(t => t.TransactionDate)
                .AsNoTracking().ToListAsync();
            if (!vendorPayment.Any()) return null;
            return vendorPayment;
        }

        public async Task<decimal> GetTotalCashInAsync(DateTime fromDate, DateTime toDate)
        {
            var start = fromDate.Date;
            var endExclusive = toDate.Date > fromDate.Date ? toDate.Date : toDate.Date.AddDays(1);
            return await _context.CashTransactions
                .Where(i => i.TransactionDate >= start && i.TransactionDate < endExclusive && i.Direction == CashDirection.In)
                .Select(t => t.Amount)
                .SumAsync();
        }

        public async Task<decimal> GetTotalCashOutAsync(DateTime fromDate, DateTime toDate)
        {
            var start = fromDate.Date;
            var endExclusive = toDate.Date > fromDate.Date ? toDate.Date : toDate.Date.AddDays(1);
            return await _context.CashTransactions
                .Where(i => i.TransactionDate >= start && i.TransactionDate < endExclusive && i.Direction == CashDirection.Out)
                .Select(t => t.Amount)
                .SumAsync();
        }
    }
}
