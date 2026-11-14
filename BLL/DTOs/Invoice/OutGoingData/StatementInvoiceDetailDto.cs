namespace BLL.DTOs.Invoice.OutGoingData
{
    /// <summary>
    /// Full posted invoice snapshot (header + item lines + discounts) for
    /// Customer (WholesaleSales) and Vendor (VendorPurchase) detailed account statements.
    /// </summary>
    public class StatementInvoiceDetailDto
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
        public int? CustomerId { get; set; }
        public int? VendorId { get; set; }
        public string PartyName { get; set; } = string.Empty;
        public decimal SubTotal { get; set; }
        public decimal ItemDiscountTotal { get; set; }
        public decimal InvoiceDiscountAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Notes { get; set; }
        public List<StatementInvoiceItemDto> Items { get; set; } = new();
    }

    public class StatementInvoiceItemDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineDiscountAmount { get; set; }
        public decimal LineTotal { get; set; }
    }
}
