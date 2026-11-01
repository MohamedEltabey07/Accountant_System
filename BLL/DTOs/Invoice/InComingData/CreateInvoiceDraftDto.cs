using System;
using System.ComponentModel.DataAnnotations;
using DAL.Enums;

namespace BLL.DTOs.Invoice.InComingData
{
    public class CreateInvoiceDraftDto
    {
        [Required]
        public InvoiceType Type { get; set; }

        [Required]
        public DateTime TransactionDate { get; set; }

        public int? CustomerId { get; set; }

        public int? VendorId { get; set; }

        // Maritime supply only: order number issued by the maritime company.
        [MaxLength(100, ErrorMessage = "رقم أمر التوريد الخارجي طويل جداً")]
        public string? ExternalOrderNumber { get; set; }

        // Maritime supply only: supply/order date received from the maritime company.
        public DateTime? SupplyOrderDate { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "The discount must be greater than or equal to 0")]
        public decimal InvoiceDiscountAmount { get; set; } = 0;

        public string? Notes { get; set; }
    }
}
