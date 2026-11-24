using BLL.DTOs.Cash.OutGoingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.OpeningBalance.OutGoingData;
using BLL.IService;
using DAL.Enums;

namespace Accountant_sys.Models
{
    /// <summary>
    /// Shared ledger loader for Customer, Vendor, and Maritime Company statements.
    /// Fetches full posted history using backend services so StatementBuilder applies
    /// deterministic chronological ordering and inclusive [from, to] filtering uniformly.
    /// </summary>
    public static class StatementLedgerLoader
    {
        public static async Task<List<StatementLine>> LoadPartyLedgerAsync(
            StatementAccountType accountType,
            int partyId,
            IOpeningBalanceService openingBalanceService,
            IInvoiceService invoiceService,
            ICreditNoteService creditNoteService,
            ICashTransactionService cashTransactionService)
        {
            var isVendor = accountType == StatementAccountType.Vendor;
            var isMaritime = accountType == StatementAccountType.MaritimeCompany;
            var historyStart = StatementBuilder.HistoryStart;
            var historyEnd = StatementBuilder.HistoryEnd;
            var labels = StatementBuilder.GetLabels(accountType);

            var lines = new List<StatementLine>();

            // 1. Opening balances
            var openings = await openingBalanceService.GetAllOpeningBalancesAsync() ?? new List<OpeningBalanceDto>();
            var matchingOpenings = isVendor
                ? openings.Where(o => o.VendorId == partyId && o.CustomerId == null)
                : openings.Where(o => o.CustomerId == partyId && o.VendorId == null);

            foreach (var opening in matchingOpenings)
            {
                var line = new StatementLine
                {
                    Date = opening.EffectiveDate,
                    Timestamp = opening.CreatedAt,
                    SortGroup = 0,
                    Kind = StatementLineKind.OpeningBalance,
                    DocumentId = opening.Id,
                    DocumentNo = "OP" + opening.Id.ToString("D4"),
                    DocumentType = "رصيد افتتاحي",
                    ShortTitle = labels.OpeningLabel,
                    SummaryHint = StatementBuilder.TruncatePreview(opening.Notes, 46),
                    Description = StatementBuilder.FormatOpeningDescription(opening),
                    Notes = opening.Notes
                };
                if (isVendor) line.Credit = opening.Amount; else line.Debit = opening.Amount;
                lines.Add(line);
            }

            // 2. Posted invoices / supply orders
            var invoiceType = accountType switch
            {
                StatementAccountType.Vendor => InvoiceType.VendorPurchase,
                StatementAccountType.MaritimeCompany => InvoiceType.MaritimeSupply,
                _ => InvoiceType.WholesaleSales
            };
            var allInvoices = await invoiceService.GetInvoicesAsync(invoiceType, InvoiceStatus.Posted) ?? new List<InvoiceDto>();
            var partyInvoices = (isVendor
                    ? allInvoices.Where(i => i.VendorId == partyId)
                    : allInvoices.Where(i => i.CustomerId == partyId))
                .ToList();

            foreach (var invoice in partyInvoices)
            {
                var docNo = invoice.InvoiceNumber;
                string? secondaryDocNo = null;
                if (isMaritime && !string.IsNullOrWhiteSpace(invoice.ExternalOrderNumber))
                {
                    docNo = invoice.ExternalOrderNumber;
                    secondaryDocNo = invoice.InvoiceNumber;
                }

                var shortTitle = labels.InvoiceRowLabel;
                var line = new StatementLine
                {
                    Date = invoice.TransactionDate,
                    Timestamp = invoice.CreatedAt,
                    Kind = isMaritime ? StatementLineKind.MaritimeSupplyOrder : StatementLineKind.Invoice,
                    DocumentId = invoice.Id,
                    DocumentNo = docNo,
                    SecondaryDocNo = secondaryDocNo,
                    DocumentType = shortTitle,
                    ShortTitle = shortTitle,
                    SummaryHint = StatementBuilder.TruncatePreview(invoice.Notes, 46),
                    Description = StatementBuilder.FormatInvoiceDescription(invoice, isVendor, isMaritime),
                    Notes = invoice.Notes
                };
                if (isVendor) line.Credit = invoice.TotalAmount; else line.Debit = invoice.TotalAmount;
                lines.Add(line);
            }

            // 3. Posted credit notes / returns linked to this party's invoices
            var cnType = isVendor ? CreditNoteType.VendorReturn : CreditNoteType.CustomerReturn;
            var returns = await creditNoteService.GetCreditNotesAsync(cnType, CreditNoteStatus.Posted, historyStart, historyEnd)
                          ?? new List<CreditNoteDto>();
            var invoiceIds = partyInvoices.Select(i => i.Id).ToHashSet();
            var returnShortTitle = labels.ReturnsLabel;

            foreach (var note in returns.Where(n => invoiceIds.Contains(n.OriginalInvoiceId)))
            {
                var line = new StatementLine
                {
                    Date = note.TransactionDate,
                    Timestamp = note.CreatedAt,
                    Kind = StatementLineKind.CreditNoteReturn,
                    DocumentId = note.Id,
                    DocumentNo = note.CreditNoteNumber,
                    DocumentType = returnShortTitle,
                    ShortTitle = returnShortTitle,
                    SummaryHint = string.IsNullOrWhiteSpace(note.OriginalInvoiceNumber)
                        ? null
                        : $"على فاتورة {note.OriginalInvoiceNumber}",
                    Description = StatementBuilder.FormatCreditNoteDescription(note),
                    Notes = note.Reason,
                    CreditNote = note
                };
                if (isVendor) line.Debit = note.TotalAmount; else line.Credit = note.TotalAmount;
                lines.Add(line);
            }

            // 4. Non-reversed payments
            var payCategory = isVendor ? CashCategory.VendorPayment : CashCategory.CustomerPayment;
            var payDirection = isVendor ? CashDirection.Out : CashDirection.In;
            var payments = await cashTransactionService.GetTransactionsAsync(historyStart, historyEnd, payCategory, payDirection)
                           ?? new List<CashTransactionDto>();
            var partyPayments = isVendor
                ? payments.Where(p => p.VendorId == partyId)
                : payments.Where(p => p.CustomerId == partyId);
            var payShortTitle = labels.PaymentRowLabel;
            var payPrefix = isVendor ? "VCH" : "REC";

            foreach (var payment in partyPayments)
            {
                var line = new StatementLine
                {
                    Date = payment.TransactionDate,
                    Timestamp = payment.CreatedAt,
                    Kind = isVendor ? StatementLineKind.VendorPayment : StatementLineKind.CustomerReceipt,
                    DocumentId = payment.Id,
                    DocumentNo = payPrefix + payment.Id.ToString("D6"),
                    DocumentType = payShortTitle,
                    ShortTitle = payShortTitle,
                    SummaryHint = StatementBuilder.FormatPaymentSummaryHint(payment),
                    Description = StatementBuilder.FormatPaymentDescription(payment, isVendor, isMaritime),
                    Notes = payment.Notes,
                    CashTransaction = payment
                };
                if (isVendor) line.Debit = payment.Amount; else line.Credit = payment.Amount;
                lines.Add(line);
            }

            // 5. Non-reversed cash refunds
            var refundCategory = isVendor ? CashCategory.VendorRefund : CashCategory.CustomerRefund;
            var refundDirection = isVendor ? CashDirection.In : CashDirection.Out;
            var refunds = await cashTransactionService.GetTransactionsAsync(historyStart, historyEnd, refundCategory, refundDirection)
                          ?? new List<CashTransactionDto>();
            var partyRefunds = isVendor
                ? refunds.Where(p => p.VendorId == partyId)
                : refunds.Where(p => p.CustomerId == partyId);
            var refundShortTitle = labels.RefundsLabel;
            var refundPrefix = isVendor ? "VRF" : "RFD";

            foreach (var refund in partyRefunds)
            {
                var line = new StatementLine
                {
                    Date = refund.TransactionDate,
                    Timestamp = refund.CreatedAt,
                    Kind = isVendor ? StatementLineKind.VendorRefund : StatementLineKind.CustomerRefund,
                    DocumentId = refund.Id,
                    DocumentNo = refundPrefix + refund.Id.ToString("D6"),
                    DocumentType = refundShortTitle,
                    ShortTitle = refundShortTitle,
                    SummaryHint = StatementBuilder.TruncatePreview(refund.Notes, 46),
                    Description = string.IsNullOrWhiteSpace(refund.Notes) ? refundShortTitle : refund.Notes.Trim(),
                    Notes = refund.Notes,
                    CashTransaction = refund
                };
                if (isVendor) line.Credit = refund.Amount; else line.Debit = refund.Amount;
                lines.Add(line);
            }

            return lines;
        }
    }
}
