using DAL.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.IRepos
{
    public interface IOpeningBalanceRepo : IGenericRepo<OpeningBalanceEntry>
    {
        Task<OpeningBalanceEntry?> GetByCustomerIdAsync(int customerId);
        Task<OpeningBalanceEntry?> GetByVendorIdAsync(int vendorId);
        Task<List<OpeningBalanceEntry>> GetAllWithPartiesAsync();
    }
}
