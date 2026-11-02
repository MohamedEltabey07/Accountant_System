using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Invoice.InComingData
{
    public class AddInvoiceItemDto
    {
        [Required]
        public int InvoiceId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required]
        [Range(0.001, double.MaxValue, ErrorMessage = "The quantity must be greater than 0")]
        public decimal Quantity { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "The unit price must be greater than or equal to 0")]
        public decimal? UnitPrice { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "The discount must be greater than or equal to 0")]
        public decimal LineDiscountAmount { get; set; } = 0;

        // Per-item supply source (MaritimeSupply drafts only): when true the item
        // is dispatched from freezer stock and is validated/deducted at posting;
        // when false the item comes from outside the freezer and never touches stock.
        public bool IsFromFreezer { get; set; }
    }
}
