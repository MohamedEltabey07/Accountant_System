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

        [Range(0, double.MaxValue, ErrorMessage = "The discount must be greater than or equal to 0")]
        public decimal InvoiceDiscountAmount { get; set; } = 0;

        public string? Notes { get; set; }
    }
}
