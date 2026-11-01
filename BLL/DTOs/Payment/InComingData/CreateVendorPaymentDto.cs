using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Payment.InComingData
{
    public class CreateVendorPaymentDto
    {
        [Required]
        public int VendorId { get; set; }

        [Required]
        public DateTime PaymentDate { get; set; } = DateTime.Today;

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")]
        public decimal Amount { get; set; }

        public string? Notes { get; set; }

        public List<InvoiceAllocationItemDto> Allocations { get; set; } = new List<InvoiceAllocationItemDto>();
    }
}
