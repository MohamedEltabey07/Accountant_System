using System;
using System.Collections.Generic;
using BLL.DTOs.Payment.OutGoingData;
using DAL.Enums;

namespace BLL.DTOs.Cash.OutGoingData
{
    public class CashTransactionDto
    {
        public int Id { get; set; }
        public DateTime TransactionDate { get; set; }
        public decimal Amount { get; set; }
        public CashDirection Direction { get; set; }
        public CashCategory Category { get; set; }

        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }

        public int? VendorId { get; set; }
        public string? VendorName { get; set; }

        public int? ExpenseCategoryId { get; set; }
        public string? ExpenseCategoryName { get; set; }

        public int? SourceInvoiceId { get; set; }
        public string? SourceInvoiceNumber { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }

        public List<PaymentAllocationDto> Allocations { get; set; } = new List<PaymentAllocationDto>();
    }
}
