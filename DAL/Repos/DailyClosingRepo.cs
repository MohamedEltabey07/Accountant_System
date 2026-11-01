using DAL.AppDbContext;
using DAL.Entities;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Repos
{
    public class DailyClosingRepo : GenericRepo<DailyClosing>, IDailyClosingRepo
    {
        private readonly AppDbcontext _context;

        public DailyClosingRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<DailyClosing?> GetByDateAsync(DateTime businessDate)
        {
            var dailyClosing = await _context.DailyClosings.Where(d => d.BusinessDate.Date == businessDate.Date)
                .Include(c => c.Transactions)
                .AsNoTracking().
                FirstOrDefaultAsync();
            if (dailyClosing == null) return null;
            return dailyClosing;
        }

        public async Task<DailyClosing?> GetLatestClosingAsync()
        {
            var lastClosingDay = await _context.DailyClosings
                .OrderByDescending(d => d.BusinessDate)
                .AsNoTracking().FirstOrDefaultAsync();
            if (lastClosingDay == null) return null;
            return lastClosingDay;
        }

        public async Task<bool> IsDateClosedAsync(DateTime date)
        {
            var isClosedDay = await _context.DailyClosings
                .Where(d => d.BusinessDate.Date == date.Date && d.IsClosed)
                .FirstOrDefaultAsync();
            if (isClosedDay == null) return false;
            return true;
        }

        public async Task<List<DailyClosing>> GetHistoryAsync(DateTime? from, DateTime? to)
        {
            var fromDate = from.HasValue ? from.Value.Date : DateTime.UtcNow.AddYears(-10);
            var toDateExclusive = to.HasValue ? to.Value.Date.AddDays(1) : DateTime.UtcNow.AddYears(1);

            var filteredClosingDays = await _context.DailyClosings
                .Where(d => d.BusinessDate >= fromDate && d.BusinessDate < toDateExclusive)
                .OrderByDescending(d => d.BusinessDate)
                .AsNoTracking()
                .ToListAsync();
            return filteredClosingDays;
        }
    }
}
