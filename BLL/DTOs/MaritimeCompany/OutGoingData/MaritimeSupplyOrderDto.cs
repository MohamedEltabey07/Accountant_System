namespace BLL.DTOs.MaritimeCompany.OutGoingData
{
    using BLL.DTOs.Invoice.OutGoingData;
    // One line of a supply order as shown in the detailed maritime account
    // statement. Every item carries its own supply-source flag.
    public class MaritimeSupplyOrderItemDto
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        // Quantity is expressed in KG on maritime supply orders.
        public decimal Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public decimal LineDiscountAmount { get; set; }

        public decimal LineTotal { get; set; }

        public bool IsFromFreezer { get; set; }
    }

    // Supply order header used by both maritime statement modes. The external
    // order number is the identifier the company itself uses to reconcile its
    // orders; it must always be visible next to the order totals.
    public class MaritimeSupplyOrderDto
    {
        public int InvoiceId { get; set; }

        public string InvoiceNumber { get; set; } = string.Empty;

        public string ExternalOrderNumber { get; set; } = string.Empty;

        public DateTime? SupplyOrderDate { get; set; }

        public DateTime TransactionDate { get; set; }

        public int CustomerId { get; set; }

        public string CompanyName { get; set; } = string.Empty;

        public decimal TotalAmount { get; set; }

        public List<MaritimeSupplyOrderItemDto> Items { get; set; } = new();

        // Additional charges visible in the detailed statement only, split by
        // whether the company is billed for them or they are internal cost.
        public List<InvoiceChargeDto> Charges { get; set; } = new();

        public decimal BillableChargesTotal { get; set; }

        public decimal InternalChargesTotal { get; set; }

        public bool HasCharges => Charges.Any();

        // Comfortable identifier used in statements: prefer the external supply
        // order number and keep the internal MAR-#### document as secondary.
        public string DisplayOrderNumber => !string.IsNullOrWhiteSpace(ExternalOrderNumber) ? ExternalOrderNumber : InvoiceNumber;
    }
}
