using BLL.DTOs.DailyClosing.InComingData;
using BLL.DTOs.DailyClosing.OutGoingData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface IDailyClosingService
    {
        Task<DailyClosingDto?> GetClosingByDateAsync(DateTime businessDate);
        Task<DailyClosingDto> CalculatePreviewClosingAsync(DateTime businessDate);
        Task<bool> PerformDailyClosingAsync(CreateDailyClosingDto dto);
        Task<bool> ReopenDailyClosingAsync(ReopenDailyClosingDto dto);
        Task<bool> IsDateClosedAsync(DateTime date);
        Task<List<DailyClosingDto>> GetClosingHistoryAsync(DateTime? from, DateTime? to);
    }
}
