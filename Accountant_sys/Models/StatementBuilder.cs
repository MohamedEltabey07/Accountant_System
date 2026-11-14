using BLL.DTOs.Cash.OutGoingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.OpeningBalance.OutGoingData;

namespace Accountant_sys.Models
{
    public static class StatementBuilder
    {
        public static readonly DateTime HistoryStart = new DateTime(2000, 1, 1);
        public static DateTime HistoryEnd => DateTime.Now.AddYears(1);

        public static StatementLineKind ResolveLineKind(StatementLine line, bool isVendor)
        {
            if (line.Kind.HasValue)
                return line.Kind.Value;

            var docType = line.DocumentType ?? string.Empty;
            if (docType.Contains("توريد"))
                return StatementLineKind.MaritimeSupplyOrder;
            if (docType.StartsWith("فاتورة") || docType.StartsWith("أمر"))
                return StatementLineKind.Invoice;
            if (docType.Contains("إشعار"))
                return StatementLineKind.CreditNoteReturn;
            if (docType.Contains("استرداد"))
                return StatementLineKind.VendorRefund;
            if (docType.StartsWith("سند رد") || docType.StartsWith("سند مرتجع"))
                return StatementLineKind.CustomerRefund;
            // Check disbursement ("صرف") BEFORE generic "دفعة" so vendor
            // payments ("سند صرف (دفعة)") are never misclassified as customer receipts.
            if (docType.Contains("صرف"))
                return StatementLineKind.VendorPayment;
            if (docType.Contains("قبض") || docType.Contains("دفعة"))
                return isVendor ? StatementLineKind.VendorPayment : StatementLineKind.CustomerReceipt;
            return StatementLineKind.OpeningBalance;
        }

        public static string FormatBalanceTag(decimal balance, bool isVendor)
        {
            if (Math.Abs(balance) < 0.005m) return "(خالص)";
            return isVendor
                ? (balance > 0 ? "(علينا)" : "(لنا عنده)")
                : (balance > 0 ? "(عليه)" : "(له)");
        }

        public static string FormatPaymentDescription(CashTransactionDto payment, bool isVendor)
        {
            var baseLabel = isVendor ? "صرف نقدي للمورد" : "تحصيل نقدي من العميل";

            string allocationDetail;
            if (payment.Allocations != null && payment.Allocations.Any())
            {
                var label = payment.Allocations.Count == 1 ? "سداد فاتورة" : "سداد فواتير";
                var parts = payment.Allocations.Select(a =>
                    string.IsNullOrWhiteSpace(a.InvoiceNumber)
                        ? $"#{a.InvoiceId} ({a.Amount:N2})"
                        : $"{a.InvoiceNumber} ({a.Amount:N2})");
                allocationDetail = $"{label}: {string.Join("، ", parts)}";
            }
            else if (!string.IsNullOrWhiteSpace(payment.SourceInvoiceNumber))
            {
                allocationDetail = $"مرتبط بفاتورة: {payment.SourceInvoiceNumber}";
            }
            else
            {
                allocationDetail = "سداد الرصيد الافتتاحي / دفعة على الحساب";
            }

            var text = $"{baseLabel} — {allocationDetail}";
            if (!string.IsNullOrWhiteSpace(payment.Notes))
                text += $" ({payment.Notes.Trim()})";
            return text;
        }

        public static string FormatInvoiceDescription(InvoiceDto invoice, bool isVendor, bool isMaritime)
        {
            var desc = isMaritime
                ? "أمر توريد بحري — بضاعة مُورَّدة"
                : (isVendor ? "بضاعة واردة (شراء من المورد)" : "بضاعة صادرة (مبيعات للعميل)");

            if (!string.IsNullOrWhiteSpace(invoice.Notes))
                desc += $" — {invoice.Notes.Trim()}";
            return desc;
        }

        public static string FormatCreditNoteDescription(CreditNoteDto note)
        {
            var desc = string.IsNullOrWhiteSpace(note.OriginalInvoiceNumber)
                ? "مرتجع بضاعة"
                : $"مرتجع على فاتورة {note.OriginalInvoiceNumber}";

            if (!string.IsNullOrWhiteSpace(note.Reason))
                desc += $" — {note.Reason.Trim()}";
            return desc;
        }

        public static string FormatOpeningDescription(OpeningBalanceDto opening)
        {
            return string.IsNullOrWhiteSpace(opening.Notes)
                ? "رصيد مسجل من حساب سابق قبل بدء المعاملات"
                : opening.Notes.Trim();
        }

        public static StatementViewModel Build(
            List<StatementLine> sourceLines,
            bool isVendor,
            int partyId,
            string partyName,
            string controllerName,
            DateTime? from,
            DateTime? to)
        {
            // True chronological ordering: calendar day -> opening balance first ->
            // actual creation timestamp -> document Id -> insertion order.
            var sorted = sourceLines
                .Select((line, index) => new { line, index })
                .OrderBy(x => x.line.Date.Date)
                .ThenBy(x => x.line.SortGroup)
                .ThenBy(x => x.line.Timestamp != default ? x.line.Timestamp : x.line.Date)
                .ThenBy(x => x.line.DocumentId ?? int.MaxValue)
                .ThenBy(x => x.index)
                .Select(x => x.line)
                .ToList();

            decimal running = 0;
            foreach (var line in sorted)
            {
                running += isVendor ? (line.Credit - line.Debit) : (line.Debit - line.Credit);
                line.RunningBalance = running;
            }

            var fromDate = from?.Date;
            var toDate = to?.Date;

            var model = new StatementViewModel
            {
                PartyId = partyId,
                PartyName = partyName,
                PartyType = isVendor ? "Vendor" : "Customer",
                ControllerName = controllerName,
                From = from,
                To = to
            };

            foreach (var line in sorted)
            {
                // Transactions before the period establish the beginning balance
                if (fromDate.HasValue && line.Date < fromDate.Value)
                {
                    model.BeginningBalance = line.RunningBalance;
                    continue;
                }

                // Transactions after the period are excluded from this statement
                if (toDate.HasValue && line.Date >= toDate.Value.AddDays(1))
                {
                    continue;
                }

                model.Lines.Add(line);

                var plus = isVendor ? line.Credit : line.Debit;
                var minus = isVendor ? line.Debit : line.Credit;
                model.TotalAdditions += plus;
                model.TotalDeductions += minus;

                switch (ResolveLineKind(line, isVendor))
                {
                    case StatementLineKind.OpeningBalance:
                        model.TotalOpening += plus;
                        break;
                    case StatementLineKind.Invoice:
                    case StatementLineKind.MaritimeSupplyOrder:
                        model.TotalInvoices += plus;
                        break;
                    case StatementLineKind.VendorRefund:
                    case StatementLineKind.CustomerRefund:
                        model.TotalRefunds += plus;
                        break;
                    case StatementLineKind.CreditNoteReturn:
                        model.TotalReturns += minus;
                        break;
                    case StatementLineKind.VendorPayment:
                    case StatementLineKind.CustomerReceipt:
                        model.TotalPayments += minus;
                        break;
                }
            }

            var last = model.Lines.LastOrDefault();
            model.EndingBalance = last != null ? last.RunningBalance : model.BeginningBalance;
            return model;
        }
    }
}
