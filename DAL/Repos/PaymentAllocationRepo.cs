using DAL.AppDbContext;
using DAL.Entities;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Repos
{
    public class PaymentAllocationRepo : GenericRepo<PaymentAllocation>, IPaymentAllocationRepo
    {
        private readonly AppDbcontext _context;

        public PaymentAllocationRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<List<PaymentAllocation>> GetAllocationsByInvoiceIdAsync(int invoiceId)
        {
            var paymentAllocationByInvoice = await _context.PaymentAllocations
                .Where(i => i.InvoiceId == invoiceId).ToListAsync();
            if (!paymentAllocationByInvoice.Any()) return null;
            return paymentAllocationByInvoice;

        }

        public async Task<List<PaymentAllocation>> GetAllocationsByCashTransactionIdAsync(int transactionId)
        {
            var paymentAllocationByCashTrans = await _context.PaymentAllocations
                .Where(i => i.CashTransactionId == transactionId).ToListAsync();
            if (!paymentAllocationByCashTrans.Any()) return null;
            return paymentAllocationByCashTrans;
        }

        public async Task AddRangeAsync(IEnumerable<PaymentAllocation> allocations)
        {
            await _context.PaymentAllocations.AddRangeAsync(allocations);
        }
    }
}
