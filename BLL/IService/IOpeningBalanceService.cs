using BLL.DTOs.OpeningBalance.InComingData;
using BLL.DTOs.OpeningBalance.OutGoingData;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface IOpeningBalanceService
    {
        Task<bool> SetCustomerOpeningBalanceAsync(CreateOpeningBalanceDto dto);
        Task<bool> SetVendorOpeningBalanceAsync(CreateOpeningBalanceDto dto);
        Task<List<OpeningBalanceDto>> GetAllOpeningBalancesAsync();
    }
}
