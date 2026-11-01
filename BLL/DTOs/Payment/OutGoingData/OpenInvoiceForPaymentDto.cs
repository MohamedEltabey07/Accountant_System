using System;
using DAL.Enums;

namespace BLL.DTOs.Payment.OutGoingData
{
    public class OpenInvoiceForPaymentDto
    {
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingBalance { get; set; }
        public InvoicePaymentStatus PaymentStatus { get; set; }
    }
}
