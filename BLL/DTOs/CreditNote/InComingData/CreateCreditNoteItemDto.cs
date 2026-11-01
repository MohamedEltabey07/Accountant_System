using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.CreditNote.InComingData
{
    public class CreateCreditNoteItemDto
    {
        [Required]
        public int ProductId { get; set; }

        [Required]
        [Range(0.001, double.MaxValue, ErrorMessage = "الكمية المرتجعة يجب أن تكون أكبر من صفر")]
        public decimal Quantity { get; set; }

        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "سعر الوحدة يجب أن يكون صفر أو أكبر")]
        public decimal UnitPrice { get; set; }
    }
}
