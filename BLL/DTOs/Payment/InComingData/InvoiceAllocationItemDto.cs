using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Payment.InComingData
{
    public class InvoiceAllocationItemDto
    {
        [Required]
        public int InvoiceId { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "مبلغ التوزيع يجب أن يكون أكبر من صفر")]
        public decimal AllocatedAmount { get; set; }
    }
}
