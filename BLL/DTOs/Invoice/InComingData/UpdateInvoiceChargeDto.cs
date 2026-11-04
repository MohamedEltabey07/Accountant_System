using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Invoice.InComingData
{
    public class UpdateInvoiceChargeDto : AddInvoiceChargeDto
    {
        [Required]
        public int ChargeId { get; set; }
    }
}
