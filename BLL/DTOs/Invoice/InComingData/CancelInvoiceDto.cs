using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Invoice.InComingData
{
    public class CancelInvoiceDto
    {
        [Required]
        public int InvoiceId { get; set; }

        [Required]
        [MaxLength(500)]
        public string CancellationReason { get; set; } = string.Empty;
    }
}
