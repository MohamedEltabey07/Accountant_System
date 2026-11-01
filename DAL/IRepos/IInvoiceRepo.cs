using DAL.Entities;
using DAL.Enums;

namespace DAL.IRepos
{
    public interface IInvoiceRepo : IGenericRepo<Invoice>
    {
        public Task<Invoice?> GetInvoiceWithItemsAsync(int invoiceId);
        public Task<InvoiceItem?> GetItemWithInvoiceAsync(int itemId);
        public Task<Invoice?> GetInvoiceWithAllocationsAndCreditNotesAsync(int invoiceId);
        public Task<List<Invoice>> GetInvoicesByTypeAndStatusAsync(InvoiceType? type, InvoiceStatus? status);
        public Task<string> GetNextInvoiceNumberAsync(InvoiceType type);
        public Task<List<Invoice>> GetUnsettledInvoicesByCustomerAsync(int customerId);
        public Task<List<Invoice>> GetUnsettledInvoicesByVendorAsync(int vendorId);
        public Task<List<Invoice>> GetCustomerOpenInvoices(int customerId);
        public  Task<List<Invoice>> GetVendorOpenInvoices(int vendorId);
        public Task<List<Invoice>> GetAllPostedInvoices();

    }
}
