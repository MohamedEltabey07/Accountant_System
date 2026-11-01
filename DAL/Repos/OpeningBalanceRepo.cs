using DAL.AppDbContext;
using DAL.Entities;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Repos
{
    public class OpeningBalanceRepo : GenericRepo<OpeningBalanceEntry>, IOpeningBalanceRepo
    {
        private readonly AppDbcontext _context;

        public OpeningBalanceRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<OpeningBalanceEntry?> GetByCustomerIdAsync(int customerId)
        {
            var openingBalanceForCst = await _context.OpeningBalanceEntries
                .Where(c => c.CustomerId == customerId).AsNoTracking().FirstOrDefaultAsync();
            return openingBalanceForCst;
        }

        public async Task<OpeningBalanceEntry?> GetByVendorIdAsync(int vendorId)
        {
            var openingBalanceForVed = await _context.OpeningBalanceEntries
                .Where(c => c.VendorId == vendorId).AsNoTracking().FirstOrDefaultAsync();
            return openingBalanceForVed;
        }

        public async Task<List<OpeningBalanceEntry>> GetAllWithPartiesAsync()
        {
            var allOpeninigBalance = await _context.OpeningBalanceEntries
                .Include(c => c.Customer)
                .Include(v => v.Vendor)
                .OrderByDescending(c => c.CreatedAt)
                .AsNoTracking().ToListAsync();
            return allOpeninigBalance;
        }
    }
}
