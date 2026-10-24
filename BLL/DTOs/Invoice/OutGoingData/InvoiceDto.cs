using System;
using System.Collections.Generic;
using DAL.Enums;

namespace BLL.DTOs.Invoice.OutGoingData
{
    public class InvoiceDto
    {
        public int Id { get; set; }

        public string InvoiceNumber { get; set; } = string.Empty;

        public InvoiceType Type { get; set; }

        public InvoiceStatus Status { get; set; }

        public DateTime TransactionDate { get; set; }

        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }

        public int? VendorId { get; set; }
        public string? VendorName { get; set; }

        public decimal SubTotal { get; set; }

        public decimal ItemDiscountTotal { get; set; }

        public decimal InvoiceDiscountAmount { get; set; }

        public decimal TotalAmount { get; set; }

        public decimal PaidAmount { get; set; }

        public decimal RemainingBalance { get; set; }

        public InvoicePaymentStatus PaymentStatus { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? PostedAt { get; set; }
        public DateTime? CancelledAt { get; set; }

        public string? CancellationReason { get; set; }

        public List<InvoiceItemDto> Items { get; set; } = new List<InvoiceItemDto>();
    }
}
