using BLL.DTOs.Payment.InComingData;
using BLL.DTOs.Payment.OutGoingData;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface IPaymentService
    {
        Task<int> RecordCustomerPaymentAsync(CreateCustomerPaymentDto dto);
        Task<int> RecordVendorPaymentAsync(CreateVendorPaymentDto dto);
        Task<List<OpenInvoiceForPaymentDto>> GetCustomerOpenInvoicesAsync(int customerId);
        Task<List<OpenInvoiceForPaymentDto>> GetVendorOpenInvoicesAsync(int vendorId);
    }
}
