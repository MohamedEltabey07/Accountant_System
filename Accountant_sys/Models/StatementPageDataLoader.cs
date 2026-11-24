using BLL.IService;
using DAL.Enums;

namespace Accountant_sys.Models
{
    /// <summary>
    /// Single read-only loader for all three account-statement screens. It keeps
    /// the three controllers identical and guarantees that supplier, customer and
    /// maritime statements consume the same ledger and settlement data.
    /// </summary>
    public static class StatementPageDataLoader
    {
        public static async Task<StatementViewModel> LoadAsync(
            StatementAccountType accountType,
            int partyId,
            string partyName,
            string controllerName,
            DateTime? from,
            DateTime? to,
            string mode,
            string? phoneNumber,
            string? address,
            IOpeningBalanceService openingBalanceService,
            IInvoiceService invoiceService,
            ICreditNoteService creditNoteService,
            ICashTransactionService cashTransactionService)
        {
            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                accountType, partyId, openingBalanceService, invoiceService,
                creditNoteService, cashTransactionService);

            var model = StatementBuilder.Build(
                ledger,
                isVendor: accountType == StatementAccountType.Vendor,
                partyId,
                partyName,
                controllerName,
                from,
                to,
                accountType,
                phoneNumber,
                address);

            model.Mode = mode;

            var invoiceType = accountType switch
            {
                StatementAccountType.Vendor => InvoiceType.VendorPurchase,
                StatementAccountType.MaritimeCompany => InvoiceType.MaritimeSupply,
                _ => InvoiceType.WholesaleSales
            };

            var customerId = accountType == StatementAccountType.Vendor ? (int?)null : partyId;
            var vendorId = accountType == StatementAccountType.Vendor ? partyId : (int?)null;

            var settlements = await invoiceService.GetStatementInvoiceSettlementsAsync(
                invoiceType, customerId, vendorId, from, to, to);

            if (accountType == StatementAccountType.MaritimeCompany)
            {
                model.SupplyOrders = await invoiceService.GetMaritimeSupplyOrdersAsync(partyId, from, to)
                                     ?? new List<BLL.DTOs.MaritimeCompany.OutGoingData.MaritimeSupplyOrderDto>();
            }
            else
            {
                // Details are loaded for both modes so the summary/detailed
                // toggle is instant (the reference toggles a body class).
                model.DetailedInvoices = await invoiceService.GetPostedInvoiceDetailsAsync(
                    invoiceType, customerId, vendorId, from, to)
                    ?? new List<BLL.DTOs.Invoice.OutGoingData.StatementInvoiceDetailDto>();
            }

            StatementPresentationBuilder.Apply(model, settlements);
            return model;
        }
    }
}
