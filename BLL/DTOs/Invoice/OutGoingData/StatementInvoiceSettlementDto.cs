using DAL.Enums;

namespace BLL.DTOs.Invoice.OutGoingData
{
    /// <summary>
    /// Lightweight posted-invoice settlement snapshot for the statement's
    /// unpaid-invoices section and per-row status chips. Settlement can be
    /// calculated as of a specific date so historical statements stay accurate.
    /// </summary>
    public class StatementInvoiceSettlementDto
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
}
