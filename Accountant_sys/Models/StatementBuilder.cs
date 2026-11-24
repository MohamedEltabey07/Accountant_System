using System.Globalization;
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

        public static StatementAccountType ResolveAccountType(bool isVendor, string? controllerName)
        {
            if (isVendor) return StatementAccountType.Vendor;
            if (string.Equals(controllerName, "MaritimeCompany", StringComparison.OrdinalIgnoreCase))
                return StatementAccountType.MaritimeCompany;
            return StatementAccountType.Customer;
        }

        public static string FormatAccountCode(StatementAccountType accountType, int partyId)
        {
            var prefix = accountType switch
            {
                StatementAccountType.Vendor => "VEN-",
                StatementAccountType.MaritimeCompany => "MAR-",
                _ => "CUS-"
            };
            return $"{prefix}{partyId:D4}";
        }

        public static string FormatQuantity(decimal quantity)
        {
            var rounded = Math.Round(quantity, 3, MidpointRounding.AwayFromZero);
            return rounded.ToString("#,##0.###", CultureInfo.InvariantCulture);
        }

        public static string FormatMoney(decimal amount)
            => amount.ToString("N2", CultureInfo.InvariantCulture);

        public static string FormatDate(DateTime date)
            => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        public static string TruncatePreview(string? text, int maxLength = 48)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var cleaned = string.Join(" ", text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (cleaned.Length <= maxLength) return cleaned;
            return cleaned[..maxLength].TrimEnd() + "…";
        }

        public static StatementLabels GetLabels(StatementAccountType accountType) => accountType switch
        {
            StatementAccountType.Vendor => new StatementLabels
            {
                StatementTitle = "كشف حساب",
                DepartmentTitle = "إدارة الحسابات والمشتريات",
                PartyRoleLabel = "المورد",
                PartyRoleShortLabel = "المورد",
                OpeningLabel = "رصيد أول المدة",
                InvoicesLabel = "فواتير الشراء",
                InvoiceRowLabel = "فاتورة شراء",
                PaymentsLabel = "المدفوع",
                PaymentRowLabel = "دفعة",
                PaidLabel = "المدفوع",
                PaidRowVerb = "سددت",
                ReturnsLabel = "مرتجع مشتريات",
                RefundsLabel = "استرداد نقدي",
                OutstandingBalanceLabel = "المتبقي علينا",
                OutstandingStatusDetail = "مستحق للمورد (علينا دفعه)",
                CreditBalanceLabel = "رصيد لنا لدى المورد",
                CreditStatusDetail = "رصيد مدفوع مقدماً لدى المورد",
                DocumentColumnLabel = "رقم المستند",
                InvoiceColumnLabel = "فاتورة",
                PaidColumnLabel = "مدفوع",
                RemainingColumnLabel = "المتبقي",
                InvoiceNumberColumnLabel = "رقم الفاتورة",
                InvoiceValueColumnLabel = "قيمة الفاتورة",
                PaidFromInvoiceColumnLabel = "اتدفع منها",
                AdditionHint = "مشتريات / رصيد مضاف",
                DeductionHint = "دفعة / مرتجع",
                BalanceHint = "كل الأرقام = المتبقي علينا · (+) يزيد المبلغ علينا · (−) مدفوع",
                PositiveBalanceTag = "(علينا)",
                NegativeBalanceTag = "(لنا عنده)",
                PartyIndexLabel = "الموردين",
                PartySignatureLabel = "توقيع المورد",
                CompanyStampLabel = "ختم الشركة",
                UnpaidSectionTitle = "فواتير لسه متسددتش",
                UnpaidTotalLabel = "إجمالي المتبقي من الفواتير",
                UnpaidHintPrefix = "حسب توزيع الدفعات المسجّل في النظام.",
                OnAccountLabel = "دفعات على الحساب (غير موزعة)",
                OnAccountRowLabel = "دفعة على الحساب",
                OnAccountHintFormat = "فيه دفعات ({0} ج.م) اتسجلت «على الحساب» ومش متوزعة على فواتير بعينها.",
                PeriodTotalLabel = "إجمالي الفترة",
                FinalBalanceLabel = "المتبقي في النهاية",
                NoteLabel = "ملاحظة"
            },
            StatementAccountType.MaritimeCompany => new StatementLabels
            {
                StatementTitle = "كشف حساب",
                DepartmentTitle = "إدارة الحسابات والتوريدات البحرية",
                PartyRoleLabel = "الشركة",
                PartyRoleShortLabel = "الشركة",
                OpeningLabel = "رصيد أول المدة",
                InvoicesLabel = "أوامر التوريد",
                InvoiceRowLabel = "أمر توريد",
                PaymentsLabel = "المحصّل",
                PaymentRowLabel = "دفعة",
                PaidLabel = "المحصّل",
                PaidRowVerb = "سددت",
                ReturnsLabel = "مرتجع / تسوية",
                RefundsLabel = "رد نقدي",
                OutstandingBalanceLabel = "المتبقي على الشركة",
                OutstandingStatusDetail = "مستحق على الشركة",
                CreditBalanceLabel = "رصيد للشركة",
                CreditStatusDetail = "رصيد دائن لصالح الشركة",
                DocumentColumnLabel = "رقم أمر التوريد / المستند",
                InvoiceColumnLabel = "فاتورة",
                PaidColumnLabel = "مدفوع",
                RemainingColumnLabel = "المتبقي",
                InvoiceNumberColumnLabel = "رقم أمر التوريد",
                InvoiceValueColumnLabel = "قيمة أمر التوريد",
                PaidFromInvoiceColumnLabel = "اتدفع منه",
                AdditionHint = "أمر توريد / إضافة",
                DeductionHint = "دفعة / تسوية",
                BalanceHint = "كل الأرقام = المتبقي على الشركة · (+) يزيد المبلغ عليها · (−) محصّل",
                PositiveBalanceTag = "(عليها)",
                NegativeBalanceTag = "(لها)",
                PartyIndexLabel = "الشركات البحرية",
                PartySignatureLabel = "توقيع الشركة",
                CompanyStampLabel = "ختم الشركة",
                UnpaidSectionTitle = "فواتير لسه متسددتش",
                UnpaidTotalLabel = "إجمالي المتبقي من الفواتير",
                UnpaidHintPrefix = "حسب توزيع التحصيلات المسجّلة في النظام.",
                OnAccountLabel = "دفعات على الحساب (غير موزعة)",
                OnAccountRowLabel = "دفعة على الحساب",
                OnAccountHintFormat = "فيه تحصيلات ({0} ج.م) اتسجلت «على الحساب» ومش متوزعة على فواتير بعينها.",
                PeriodTotalLabel = "إجمالي الفترة",
                FinalBalanceLabel = "المتبقي في النهاية",
                NoteLabel = "ملاحظة"
            },
            _ => new StatementLabels
            {
                StatementTitle = "كشف حساب",
                DepartmentTitle = "إدارة الحسابات والمبيعات",
                PartyRoleLabel = "العميل",
                PartyRoleShortLabel = "العميل",
                OpeningLabel = "رصيد أول المدة",
                InvoicesLabel = "فواتير البيع",
                InvoiceRowLabel = "فاتورة بيع",
                PaymentsLabel = "المحصّل",
                PaymentRowLabel = "دفعة",
                PaidLabel = "المحصّل",
                PaidRowVerb = "سددت",
                ReturnsLabel = "مرتجع مبيعات",
                RefundsLabel = "رد نقدي",
                OutstandingBalanceLabel = "المتبقي على العميل",
                OutstandingStatusDetail = "مستحق على العميل",
                CreditBalanceLabel = "رصيد للعميل",
                CreditStatusDetail = "رصيد دائن لصالح العميل",
                DocumentColumnLabel = "رقم المستند",
                InvoiceColumnLabel = "فاتورة",
                PaidColumnLabel = "مدفوع",
                RemainingColumnLabel = "المتبقي",
                InvoiceNumberColumnLabel = "رقم الفاتورة",
                InvoiceValueColumnLabel = "قيمة الفاتورة",
                PaidFromInvoiceColumnLabel = "اتدفع منها",
                AdditionHint = "مبيعات / إضافة",
                DeductionHint = "دفعة / مرتجع",
                BalanceHint = "كل الأرقام = المتبقي على العميل · (+) يزيد المبلغ عليه · (−) محصّل",
                PositiveBalanceTag = "(عليه)",
                NegativeBalanceTag = "(له)",
                PartyIndexLabel = "العملاء",
                PartySignatureLabel = "توقيع العميل",
                CompanyStampLabel = "ختم الشركة",
                UnpaidSectionTitle = "فواتير لسه متسددتش",
                UnpaidTotalLabel = "إجمالي المتبقي من الفواتير",
                UnpaidHintPrefix = "حسب توزيع التحصيلات المسجّلة في النظام.",
                OnAccountLabel = "دفعات على الحساب (غير موزعة)",
                OnAccountRowLabel = "دفعة على الحساب",
                OnAccountHintFormat = "فيه تحصيلات ({0} ج.م) اتسجلت «على الحساب» ومش متوزعة على فواتير بعينها.",
                PeriodTotalLabel = "إجمالي الفترة",
                FinalBalanceLabel = "المتبقي في النهاية",
                NoteLabel = "ملاحظة"
            }
        };

        public static StatementLineKind ResolveLineKind(StatementLine line, bool isVendor)
        {
            if (line.Kind.HasValue)
                return line.Kind.Value;

            var docType = line.DocumentType ?? string.Empty;
            if (docType.Contains("توريد"))
                return StatementLineKind.MaritimeSupplyOrder;
            if (docType.StartsWith("فاتورة") || docType.StartsWith("أمر"))
                return StatementLineKind.Invoice;
            if (docType.Contains("إشعار") || docType.Contains("مرتجع"))
                return StatementLineKind.CreditNoteReturn;
            if (docType.Contains("استرداد"))
                return StatementLineKind.VendorRefund;
            if (docType.StartsWith("سند رد") || docType.StartsWith("رد نقدي"))
                return StatementLineKind.CustomerRefund;
            // Check disbursement ("صرف") BEFORE generic "دفعة" so vendor
            // payments are never misclassified as customer receipts.
            if (docType.Contains("صرف") || docType.Contains("للمورد"))
                return StatementLineKind.VendorPayment;
            if (docType.Contains("قبض") || docType.Contains("دفعة"))
                return isVendor ? StatementLineKind.VendorPayment : StatementLineKind.CustomerReceipt;
            return StatementLineKind.OpeningBalance;
        }

        public static string ResolveShortTitle(StatementLine line, StatementAccountType accountType)
        {
            if (!string.IsNullOrWhiteSpace(line.ShortTitle))
                return line.ShortTitle;

            var labels = GetLabels(accountType);
            return ResolveLineKind(line, accountType == StatementAccountType.Vendor) switch
            {
                StatementLineKind.OpeningBalance => labels.OpeningLabel,
                StatementLineKind.MaritimeSupplyOrder => labels.InvoiceRowLabel,
                StatementLineKind.Invoice => labels.InvoiceRowLabel,
                StatementLineKind.VendorPayment => labels.PaymentRowLabel,
                StatementLineKind.CustomerReceipt => labels.PaymentRowLabel,
                StatementLineKind.CreditNoteReturn => labels.ReturnsLabel,
                StatementLineKind.VendorRefund => labels.RefundsLabel,
                StatementLineKind.CustomerRefund => labels.RefundsLabel,
                _ => string.IsNullOrWhiteSpace(line.DocumentType) ? "حركة مالية" : line.DocumentType
            };
        }

        public static string FormatBalanceTag(decimal balance, bool isVendor)
        {
            if (Math.Abs(balance) < 0.005m) return "(خالص)";
            return isVendor
                ? (balance > 0 ? "(علينا)" : "(لنا عنده)")
                : (balance > 0 ? "(عليه)" : "(له)");
        }

        public static string FormatPaymentSummaryHint(CashTransactionDto payment)
        {
            if (payment.Allocations != null && payment.Allocations.Any())
            {
                if (payment.Allocations.Count == 1)
                {
                    var only = payment.Allocations[0];
                    var invNo = string.IsNullOrWhiteSpace(only.InvoiceNumber) ? $"#{only.InvoiceId}" : only.InvoiceNumber;
                    return $"سداد {invNo}";
                }
                return $"موزعة على {payment.Allocations.Count} فواتير";
            }

            if (!string.IsNullOrWhiteSpace(payment.SourceInvoiceNumber))
                return $"مرتبطة بفاتورة {payment.SourceInvoiceNumber}";

            if (!string.IsNullOrWhiteSpace(payment.Notes))
                return TruncatePreview(payment.Notes, 46);

            return "دفعة على الحساب";
        }

        public static string FormatPaymentDescription(CashTransactionDto payment, bool isVendor, bool isMaritime = false)
        {
            var baseLabel = isVendor
                ? "دفعة للمورد"
                : (isMaritime ? "دفعة من الشركة البحرية" : "دفعة من العميل");

            string allocationDetail;
            if (payment.Allocations != null && payment.Allocations.Any())
            {
                var label = payment.Allocations.Count == 1 ? "سداد فاتورة" : "توزيع على فواتير";
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
                allocationDetail = "دفعة على الحساب / الرصيد الافتتاحي";
            }

            return $"{baseLabel} — {allocationDetail}";
        }

        public static string FormatInvoiceDescription(InvoiceDto invoice, bool isVendor, bool isMaritime)
        {
            return isMaritime
                ? "أمر توريد"
                : (isVendor ? "فاتورة مشتريات" : "فاتورة مبيعات");
        }

        public static string FormatCreditNoteDescription(CreditNoteDto note)
        {
            var desc = string.IsNullOrWhiteSpace(note.OriginalInvoiceNumber)
                ? "مرتجع بضاعة"
                : $"مرتجع على {note.OriginalInvoiceNumber}";

            if (!string.IsNullOrWhiteSpace(note.Reason))
                desc += $" — {note.Reason.Trim()}";
            return desc;
        }

        public static string FormatOpeningDescription(OpeningBalanceDto opening)
        {
            return string.IsNullOrWhiteSpace(opening.Notes)
                ? "رصيد افتتاحي مسجل قبل بدء الفترة"
                : opening.Notes.Trim();
        }

        public static StatementViewModel Build(
            List<StatementLine> sourceLines,
            bool isVendor,
            int partyId,
            string partyName,
            string controllerName,
            DateTime? from,
            DateTime? to,
            StatementAccountType? accountType = null,
            string? phoneNumber = null,
            string? address = null)
        {
            var resolvedType = accountType ?? ResolveAccountType(isVendor, controllerName);
            var resolvedIsVendor = resolvedType == StatementAccountType.Vendor;

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
                running += resolvedIsVendor ? (line.Credit - line.Debit) : (line.Debit - line.Credit);
                line.RunningBalance = running;
            }

            var fromDate = from?.Date;
            var toDate = to?.Date;

            var model = new StatementViewModel
            {
                PartyId = partyId,
                PartyName = partyName,
                PartyType = resolvedIsVendor ? "Vendor" : "Customer",
                AccountType = resolvedType,
                AccountCode = FormatAccountCode(resolvedType, partyId),
                PartyPhoneNumber = phoneNumber,
                PartyAddress = address,
                ControllerName = controllerName,
                From = from,
                To = to
            };

            foreach (var line in sorted)
            {
                // Transactions strictly before the reporting window establish the carry-forward balance
                if (fromDate.HasValue && line.Date.Date < fromDate.Value)
                {
                    model.BeginningBalance = line.RunningBalance;
                    continue;
                }

                // Transactions strictly after the reporting window are excluded
                if (toDate.HasValue && line.Date.Date > toDate.Value)
                {
                    continue;
                }

                model.Lines.Add(line);

                var plus = resolvedIsVendor ? line.Credit : line.Debit;
                var minus = resolvedIsVendor ? line.Debit : line.Credit;
                model.TotalAdditions += plus;
                model.TotalDeductions += minus;

                switch (ResolveLineKind(line, resolvedIsVendor))
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
