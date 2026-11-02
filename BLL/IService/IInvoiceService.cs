using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using DAL.Enums;

namespace BLL.IService
{
    public interface IInvoiceService
    {
        Task<int> CreateDraftInvoiceAsync(CreateInvoiceDraftDto dto);
        Task<bool> AddItemToDraftAsync(AddInvoiceItemDto dto);
        Task<bool> UpdateDraftItemAsync(UpdateInvoiceItemDto dto);
        Task<bool> RemoveItemFromDraftAsync(int itemId);
        Task<bool> PostInvoiceAsync(int invoiceId);
        Task<decimal> GetInvoiceRemainingBalanceAsync(int invoiceId);
        Task<InvoicePaymentStatus> GetInvoicePaymentStatusAsync(int invoiceId);
        Task<bool> CancelPostedInvoiceAsync(CancelInvoiceDto dto);
        Task<InvoiceDto?> GetInvoiceByIdAsync(int id);
        Task<List<InvoiceDto>> GetInvoicesAsync(InvoiceType? type, InvoiceStatus? status);
        // Posted maritime supply orders (with items) for account statements.
        // Used by BOTH statement modes so summary and detailed totals are
        // computed from the exact same source.
        Task<List<MaritimeSupplyOrderDto>> GetMaritimeSupplyOrdersAsync(int? customerId, DateTime? from, DateTime? to);
    }
}
