namespace Accountant_sys.Models
{
    public class StatementLine
    {
        public DateTime Date { get; set; }
        public string DocumentNo { get; set; } = string.Empty;
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
