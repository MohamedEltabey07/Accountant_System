using DAL.Entities;
using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.IRepos
{
    public interface ICreditNoteRepo : IGenericRepo<CreditNote>
    {
        Task<CreditNote?> GetByIdWithItemsAsync(int id);
        Task<List<CreditNote>> GetFilteredCreditNotesAsync(CreditNoteType? type, CreditNoteStatus? status, DateTime? from, DateTime? to);
        Task<string> GetNextCreditNoteNumberAsync();
        Task<Dictionary<int, decimal>> GetTotalReturnedQtyForInvoiceItemAsync(int invoiceId);
    }
}
