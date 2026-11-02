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

    public class StatementLine
    {
        public DateTime Date { get; set; }
        public string DocumentNo { get; set; } = string.Empty;
        // Internal document number rendered as a secondary label when the
        // primary number is external (maritime supply orders show the external
        // supply order number with the internal MAR-#### underneath).
        public string? SecondaryDocNo { get; set; }
        public int? DocumentId { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal RunningBalance { get; set; }
    }

    public class StatementViewModel
    {
        public int PartyId { get; set; }
        public string PartyName { get; set; } = string.Empty;
        public string PartyType { get; set; } = "Customer";
        public string ControllerName { get; set; } = string.Empty;
        // Statement display mode — used by the maritime statement to switch
        // between the summary ledger ("summary") and the detailed supply
        // orders format ("detailed"). Null for the generic customer/vendor flow.
        public string Mode { get; set; } = "summary";
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public decimal BeginningBalance { get; set; }
        public decimal TotalInvoices { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal TotalPayments { get; set; }
        public decimal EndingBalance { get; set; }
        public List<StatementLine> Lines { get; set; } = new();
    }
}
