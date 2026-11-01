using BLL.DTOs.MaritimeCompany.InComingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface IMaritimeCompanyService
    {
        Task<bool> CreateCompanyAsync(CreateMaritimeCompanyDto dto);

        Task<bool> UpdateCompanyAsync(UpdateMaritimeCompanyDto dto);

        Task<bool> ToggleStatusAsync(int id);

        Task<bool> DeleteCompanyAsync(int id);

        Task<List<MaritimeCompanyDto>> GetAllCompaniesAsync();

        Task<List<MaritimeCompanyDto>> GetActiveCompaniesAsync();

        Task<MaritimeCompanyDto?> GetCompanyByIdAsync(int id);

        Task<decimal> GetCompanyBalanceAsync(int companyId);
    }
}
