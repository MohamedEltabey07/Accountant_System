using DAL.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.IRepos
{
    public interface IPaymentAllocationRepo : IGenericRepo<PaymentAllocation>
    {
        Task<List<PaymentAllocation>> GetAllocationsByInvoiceIdAsync(int invoiceId);
        Task<List<PaymentAllocation>> GetAllocationsByCashTransactionIdAsync(int transactionId);
        Task AddRangeAsync(IEnumerable<PaymentAllocation> allocations);
    }
}
