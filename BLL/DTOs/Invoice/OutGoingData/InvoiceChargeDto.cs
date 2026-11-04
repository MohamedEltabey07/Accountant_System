namespace BLL.DTOs.Invoice.OutGoingData
{
    public class InvoiceChargeDto
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }

        public string Description { get; set; } = string.Empty;

        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }

        public decimal Amount { get; set; }

        public bool IsBillableToCompany { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
