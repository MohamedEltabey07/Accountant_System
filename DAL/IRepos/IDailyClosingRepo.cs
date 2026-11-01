using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.IRepos
{
    public interface IDailyClosingRepo : IGenericRepo<DailyClosing>
    {
        Task<DailyClosing?> GetByDateAsync(DateTime businessDate);
        Task<DailyClosing?> GetLatestClosingAsync();
        Task<bool> IsDateClosedAsync(DateTime date);
        Task<List<DailyClosing>> GetHistoryAsync(DateTime? from, DateTime? to);
    }
}
