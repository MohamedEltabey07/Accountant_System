using BLL.DTOs.Cash.OutGoingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using DAL.Enums;

namespace Accountant_sys.Models
{
    // Active-status filter used by operational index pages. The default view
    // lists ACTIVE records only; "inactive" / "all" remain available for
    // management (viewing and re-activating). Backend-filtered per request.
    public static class ActiveStatusFilter
    {
        public static bool? ToIsActive(string? status)
            => status switch
            {
                "active" => true,
                "inactive" => false,
                _ => null
            };

        public static string Normalize(string? status)
            => status switch
            {
                "active" => "active",
                "inactive" => "inactive",
                _ => "all"
            };
    }

    public enum StatementAccountType
    {
        Customer,
        Vendor,
        MaritimeCompany
    }

    public enum StatementLineKind
    {
        OpeningBalance,
        Invoice,
        MaritimeSupplyOrder,
        VendorPayment,
        CustomerReceipt,
        CreditNoteReturn,
        VendorRefund,
        CustomerRefund
    }

    public class StatementLine
    {
        public DateTime Date { get; set; }
        public DateTime Timestamp { get; set; }
        // 0 = Opening Balance (always first on its date), 1 = Regular movement
        public int SortGroup { get; set; } = 1;
        public StatementLineKind? Kind { get; set; }
        public string DocumentNo { get; set; } = string.Empty;
        // Internal document number rendered as a secondary label when the
        // primary number is external (maritime supply orders show the external
        // supply order number with the internal MAR-#### underneath).
        public string? SecondaryDocNo { get; set; }
        public int? DocumentId { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        // Short operation label used in the unified ledger row (e.g., فاتورة مشتريات).
        public string ShortTitle { get; set; } = string.Empty;
        // Concise secondary summary hint (e.g., allocation count or short note preview).
        public string? SummaryHint { get; set; }
        // Full explanation / details preserved for detailed mode and auditing.
        public string Description { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public CashTransactionDto? CashTransaction { get; set; }
        public CreditNoteDto? CreditNote { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal RunningBalance { get; set; }
    }

    public sealed class StatementLabels
    {
        public required string StatementTitle { get; init; }
        public required string DepartmentTitle { get; init; }
        public required string PartyRoleLabel { get; init; }
        public required string PartyRoleShortLabel { get; init; }
        public required string OpeningLabel { get; init; }
        public required string InvoicesLabel { get; init; }
        public required string InvoiceRowLabel { get; init; }
        public required string PaymentsLabel { get; init; }
        public required string PaymentRowLabel { get; init; }
        public required string PaidLabel { get; init; }
        public required string PaidRowVerb { get; init; }
        public required string ReturnsLabel { get; init; }
        public required string RefundsLabel { get; init; }
        public required string OutstandingBalanceLabel { get; init; }
        public required string OutstandingStatusDetail { get; init; }
        public required string CreditBalanceLabel { get; init; }
        public required string CreditStatusDetail { get; init; }
        public required string DocumentColumnLabel { get; init; }
        public required string InvoiceColumnLabel { get; init; }
        public required string PaidColumnLabel { get; init; }
        public required string RemainingColumnLabel { get; init; }
        public required string InvoiceNumberColumnLabel { get; init; }
        public required string InvoiceValueColumnLabel { get; init; }
        public required string PaidFromInvoiceColumnLabel { get; init; }
        public required string AdditionHint { get; init; }
        public required string DeductionHint { get; init; }
        public required string BalanceHint { get; init; }
        public required string PositiveBalanceTag { get; init; }
        public required string NegativeBalanceTag { get; init; }
        public required string PartyIndexLabel { get; init; }
        public required string PartySignatureLabel { get; init; }
        public required string CompanyStampLabel { get; init; }
        public required string UnpaidSectionTitle { get; init; }
        public required string UnpaidTotalLabel { get; init; }
        public required string UnpaidHintPrefix { get; init; }
        public required string OnAccountLabel { get; init; }
        public required string OnAccountRowLabel { get; init; }
        public required string OnAccountHintFormat { get; init; }
        public required string PeriodTotalLabel { get; init; }
        public required string FinalBalanceLabel { get; init; }
        public required string NoteLabel { get; init; }
    }

    /// <summary>One posted invoice with its settlement as of the statement end date.</summary>
    public sealed class StatementUnpaidInvoice
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal ReturnsAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public InvoicePaymentStatus PaymentStatus { get; set; }
    }

    /// <summary>Settlement snapshot used by movement-row status chips.</summary>
    public sealed class StatementInvoiceSettlement
    {
        public decimal PaidAmount { get; set; }
        public decimal ReturnsAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public InvoicePaymentStatus PaymentStatus { get; set; }
        public decimal SettledPercent => PaidAmount + ReturnsAmount <= 0
            ? 0
            : Math.Clamp((PaidAmount + ReturnsAmount) / Math.Max(0.01m, PaidAmount + ReturnsAmount + RemainingAmount) * 100m, 0m, 100m);
    }

    public class StatementViewModel
    {
        public int PartyId { get; set; }
        public string PartyName { get; set; } = string.Empty;
        public string PartyType { get; set; } = "Customer";
        public StatementAccountType AccountType { get; set; } = StatementAccountType.Customer;
        public string AccountCode { get; set; } = string.Empty;
        public string? PartyPhoneNumber { get; set; }
        public string? PartyAddress { get; set; }
        public string ControllerName { get; set; } = string.Empty;
        public string Mode { get; set; } = "summary";
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public decimal BeginningBalance { get; set; }
        public decimal TotalOpening { get; set; }
        public decimal TotalInvoices { get; set; }
        public decimal TotalRefunds { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal TotalPayments { get; set; }
        // Exact column totals across all Lines in the statement period:
        // EndingBalance == BeginningBalance + TotalAdditions - TotalDeductions
        public decimal TotalAdditions { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal EndingBalance { get; set; }
        public List<StatementLine> Lines { get; set; } = new();
        public List<StatementInvoiceDetailDto> DetailedInvoices { get; set; } = new();
        public List<MaritimeSupplyOrderDto> SupplyOrders { get; set; } = new();

        // ---- Presentation-only values (never used for accounting math) ----
        public decimal OpeningDisplay { get; set; }
        public List<StatementUnpaidInvoice> UnpaidInvoices { get; set; } = new();
        public decimal UnpaidTotal { get; set; }
        public decimal OnAccountTotal { get; set; }
        public decimal OverpaymentTotal { get; set; }
        public decimal CrossPeriodBridge { get; set; }
        public decimal? LastPaymentAmount { get; set; }
        public DateTime? LastPaymentDate { get; set; }
        public Dictionary<int, StatementInvoiceSettlement> InvoiceSettlements { get; set; } = new();

        public bool IsVendor => AccountType == StatementAccountType.Vendor;
        public bool IsMaritime => AccountType == StatementAccountType.MaritimeCompany;
        public bool IsDetailed => string.Equals(Mode, "detailed", StringComparison.OrdinalIgnoreCase);
        public bool ShowExternalOrderColumn => IsMaritime;
        public bool HasUnpaidInvoices => UnpaidInvoices.Count > 0;
        public bool HasReturns => TotalReturns > 0.005m;
        public bool HasRefunds => TotalRefunds > 0.005m;
        public bool HasOverpayment => OverpaymentTotal > 0.005m;
        public bool HasBridge => Math.Abs(CrossPeriodBridge) >= 0.005m;
        public bool IsSettled => Math.Abs(EndingBalance) < 0.005m;
        public bool IsCreditBalance => EndingBalance < -0.005m;
        public string HeroBalanceLabel => IsCreditBalance ? Labels.CreditBalanceLabel : Labels.OutstandingBalanceLabel;

        public StatementLabels Labels => StatementBuilder.GetLabels(AccountType);
    }
}
