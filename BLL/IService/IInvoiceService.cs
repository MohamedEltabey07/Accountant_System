using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Invoice.OutGoingData;
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
    }
}
