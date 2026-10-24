namespace BLL.DTOs.Invoice.OutGoingData
{
    public class InvoiceItemDto
    {
        public int Id { get; set; }

        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public decimal Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal LineDiscountAmount { get; set; }

        public decimal LineTotal { get; set; }
    }
}
