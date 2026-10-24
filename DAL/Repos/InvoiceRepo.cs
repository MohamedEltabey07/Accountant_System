using DAL.AppDbContext;
using DAL.Entities;
using DAL.Enums;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repos
{
    public class InvoiceRepo : GenericRepo<Invoice>, IInvoiceRepo
    {
        private readonly AppDbcontext _context;

        public InvoiceRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<Invoice?> GetInvoiceWithItemsAsync(int invoiceId)
        {
            return await _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.Vendor)
                .Include(i => i.Items)
                    .ThenInclude(ii => ii.Product)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);
        }

        public async Task<InvoiceItem?> GetItemWithInvoiceAsync(int itemId)
        {
            return await _context.InvoiceItems
                .Include(ii => ii.Invoice)
                    .ThenInclude(i => i.Items)
                .FirstOrDefaultAsync(ii => ii.Id == itemId);
        }

        public async Task<Invoice?> GetInvoiceWithAllocationsAndCreditNotesAsync(int invoiceId)
        {
            return await _context.Invoices
                .Include(i => i.PaymentAllocations)
                .Include(i => i.CreditNotes)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);
        }

        public async Task<List<Invoice>> GetInvoicesByTypeAndStatusAsync(InvoiceType? type, InvoiceStatus? status)
        {
            var query = _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.Vendor)
                .AsQueryable();

            if (type.HasValue)
                query = query.Where(i => i.Type == type.Value);

            if (status.HasValue)
                query = query.Where(i => i.Status == status.Value);

            return await query
                .OrderByDescending(i => i.TransactionDate)
                .ToListAsync();
        }

        public async Task<string> GetNextInvoiceNumberAsync(InvoiceType type)
        {
            var prefix = type switch
            {
                InvoiceType.VendorPurchase => "PUR-",
                InvoiceType.WholesaleSales => "SAL-",
                InvoiceType.StoreCashSales => "RET-",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown invoice type.")
            };

            var existingNumbers = await _context.Invoices
                .Where(i => i.InvoiceNumber.StartsWith(prefix))
                .Select(i => i.InvoiceNumber)
                .ToListAsync();

            var maxNumber = 0;
            foreach (var invoiceNumber in existingNumbers)
            {
                var sequentialPart = invoiceNumber.Split('-').LastOrDefault();
                if (int.TryParse(sequentialPart, out var number) && number > maxNumber)
                    maxNumber = number;
            }

            return $"{prefix}{maxNumber + 1:D4}";
        }

        public async Task<List<Invoice>> GetUnsettledInvoicesByCustomerAsync(int customerId)
        {
            return await _context.Invoices
                .Where(i =>
                    i.CustomerId == customerId &&
                    i.Type == InvoiceType.WholesaleSales &&
                    i.Status == InvoiceStatus.Posted &&
                    i.TotalAmount
                        - (i.PaymentAllocations.Sum(pa => (decimal?)pa.Amount) ?? 0m)
                        - (i.CreditNotes
                            .Where(cn => cn.Status == CreditNoteStatus.Posted)
                            .Sum(cn => (decimal?)cn.TotalAmount) ?? 0m) > 0)
                .OrderByDescending(i => i.TransactionDate)
                .ToListAsync();
        }

        public async Task<List<Invoice>> GetUnsettledInvoicesByVendorAsync(int vendorId)
        {
            return await _context.Invoices
                .Where(i =>
                    i.VendorId == vendorId &&
                    i.Type == InvoiceType.VendorPurchase &&
                    i.Status == InvoiceStatus.Posted &&
                    i.TotalAmount
                        - (i.PaymentAllocations.Sum(pa => (decimal?)pa.Amount) ?? 0m)
                        - (i.CreditNotes
                            .Where(cn => cn.Status == CreditNoteStatus.Posted)
                            .Sum(cn => (decimal?)cn.TotalAmount) ?? 0m) > 0)
                .OrderByDescending(i => i.TransactionDate)
                .ToListAsync();
        }
    }
}

