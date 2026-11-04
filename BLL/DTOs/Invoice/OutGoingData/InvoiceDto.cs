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
        public CustomerType CustomerType { get; set; }

        public int? VendorId { get; set; }
        public string? VendorName { get; set; }

        // Maritime supply only
        public string? ExternalOrderNumber { get; set; }
        public DateTime? SupplyOrderDate { get; set; }

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

        // Order-level additional charges (maritime supply orders).
        public List<InvoiceChargeDto> AdditionalCharges { get; set; } = new List<InvoiceChargeDto>();

        // Sum of billable charges (already included in TotalAmount).
        public decimal BillableChargesTotal { get; set; }

        // Sum of internal-only charges (tracked cost, never billed to the company).
        public decimal InternalChargesTotal { get; set; }

        // Audit trail of the controlled amendments applied to this posted order.
        public List<InvoiceAmendmentDto> Amendments { get; set; } = new List<InvoiceAmendmentDto>();
    }

    public class InvoiceAmendmentDto
    {
        public int Id { get; set; }
        public DateTime AmendedAtUtc { get; set; }
        public string? AmendedByUserId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
