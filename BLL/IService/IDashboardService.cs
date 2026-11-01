using BLL.DTOs.Dashboard.OutGoingData;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface IDashboardService
    {
        Task<DashboardDto> GetDashboardMetricsAsync(decimal openingCashAmount = 0m);
    }
}
