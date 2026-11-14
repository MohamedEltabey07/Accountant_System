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
        Task<bool> AddChargeToDraftAsync(AddInvoiceChargeDto dto);
        Task<bool> UpdateDraftChargeAsync(UpdateInvoiceChargeDto dto);
        Task<bool> RemoveChargeFromDraftAsync(int chargeId);
        Task<bool> PostInvoiceAsync(int invoiceId);
        Task<decimal> GetInvoiceRemainingBalanceAsync(int invoiceId);
        Task<InvoicePaymentStatus> GetInvoicePaymentStatusAsync(int invoiceId);
        Task<bool> CancelPostedInvoiceAsync(CancelInvoiceDto dto);
        Task<bool> AmendPostedMaritimeInvoiceAsync(AmendPostedMaritimeInvoiceDto dto, string? userId = null);
        Task<bool> DeleteDraftInvoiceAsync(int invoiceId);
        Task<InvoiceDto?> GetInvoiceByIdAsync(int id);
        Task<List<InvoiceDto>> GetInvoicesAsync(InvoiceType? type, InvoiceStatus? status);
        // Posted maritime supply orders (with items) for account statements.
        // Used by BOTH statement modes so summary and detailed totals are
        // computed from the exact same source.
        Task<List<MaritimeSupplyOrderDto>> GetMaritimeSupplyOrdersAsync(int? customerId, DateTime? from, DateTime? to);
        // Posted customer (WholesaleSales) or vendor (VendorPurchase) invoices
        // with full item details for detailed account statements.
        Task<List<StatementInvoiceDetailDto>> GetPostedInvoiceDetailsAsync(
            InvoiceType type,
            int? customerId,
            int? vendorId,
            DateTime? from,
            DateTime? to);
    }
}
