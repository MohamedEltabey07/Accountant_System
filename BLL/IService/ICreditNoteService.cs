using BLL.DTOs.CreditNote.InComingData;
using BLL.DTOs.CreditNote.OutGoingData;
using DAL.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface ICreditNoteService
    {
        Task<int> CreateDraftCreditNoteAsync(CreateCreditNoteDraftDto dto);
        Task<bool> PostCreditNoteAsync(PostCreditNoteDto dto);
        Task<CreditNoteDto?> GetCreditNoteByIdAsync(int id);
        Task<List<CreditNoteDto>> GetCreditNotesAsync(CreditNoteType? type, CreditNoteStatus? status,DateTime from , DateTime to);
    }
}
