using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.CreditNote.InComingData
{
    public class PostCreditNoteDto
    {
        [Required]
        public int CreditNoteId { get; set; }

        public bool IsCashRefund { get; set; } = false;
    }
}
