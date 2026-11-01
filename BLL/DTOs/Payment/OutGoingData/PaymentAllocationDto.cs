using System;

namespace BLL.DTOs.Payment.OutGoingData
{
    public class PaymentAllocationDto
    {
        public int Id { get; set; }
        public int CashTransactionId { get; set; }
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
