using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Invoice.InComingData
{
    public class UpdateInvoiceItemDto
    {
        [Required]
        public int ItemId { get; set; }

        [Required]
        [Range(0.001, double.MaxValue, ErrorMessage = "The quantity must be greater than 0")]
        public decimal Quantity { get; set; }

        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "The unit price must be greater than or equal to 0")]
        public decimal UnitPrice { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "The discount must be greater than or equal to 0")]
        public decimal LineDiscountAmount { get; set; }
    }
}
