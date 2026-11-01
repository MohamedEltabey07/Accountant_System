namespace Accountant_sys.Models
{
    public class CreditNoteLineViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal OriginalQuantity { get; set; }
        public decimal AlreadyReturned { get; set; }
        public decimal MaxReturnable { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
