using DAL.AppDbContext;
using DAL.Entities;
using DAL.Enums;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Repos
{
    public class CreditNoteRepo : GenericRepo<CreditNote>, ICreditNoteRepo
    {
        private readonly AppDbcontext _context;

        public CreditNoteRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<CreditNote?> GetByIdWithItemsAsync(int id)
        {
            var creditNotesItems = await _context.CreditNotes.Where(i =>  i.Id == id)
                .Include(c => c.Items).Include(o => o.OriginalInvoice).FirstOrDefaultAsync();
            return creditNotesItems;
        }

        public async Task<List<CreditNote>> GetFilteredCreditNotesAsync(CreditNoteType? type, CreditNoteStatus? status, DateTime? from, DateTime? to)
        {
            var query = _context.CreditNotes.Include(t => t.Items).Include(i => i.OriginalInvoice).AsQueryable();

            if (type.HasValue) query = query.Where(c => c.Type == type.Value);
            if (status.HasValue) query = query.Where(c => c.Status == status.Value);
            if (from.HasValue)
            {
                var fromDate = from.Value.Date;
                query = query.Where(c => c.CreatedAt >= fromDate);
            }
            if (to.HasValue)
            {
                var toDateExclusive = to.Value.Date.AddDays(1);
                query = query.Where(c => c.CreatedAt < toDateExclusive);
            }

            return await query.OrderByDescending(c => c.CreatedAt).AsNoTracking().ToListAsync();
        }

        public async Task<string> GetNextCreditNoteNumberAsync()
        {
            const string prefix = "CN-";
            var lastNumber = await _context.CreditNotes
                .OrderByDescending(c => c.Id)
                .Select(c => c.CreditNoteNumber)
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(lastNumber))
            {
                return $"{prefix}0001";
            }
            var sequentialPart = lastNumber.Split('-').LastOrDefault();
            if (int.TryParse(sequentialPart, out var number))
            {
                return $"{prefix}{number + 1:D4}";
            }
            return $"{prefix}0001";
        }

        public async Task<Dictionary<int, decimal>> GetTotalReturnedQtyForInvoiceItemAsync(int invoiceId)
        {
            var invoiceItemsDetials = await _context.CreditNoteItems
         .Where(item => item.CreditNote.OriginalInvoiceId == invoiceId
                     && item.CreditNote.Status == CreditNoteStatus.Posted
                    ).GroupBy(x => x.ProductId)
                    .Select(o => new {ProductId = o.Key , Quantity = o.Sum(q => q.Quantity) })
                    .ToDictionaryAsync(i => i.ProductId, x => x.Quantity);
            return invoiceItemsDetials;
        }
    }
}
