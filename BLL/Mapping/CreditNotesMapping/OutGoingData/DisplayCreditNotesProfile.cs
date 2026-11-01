using AutoMapper;
using BLL.DTOs.CreditNote.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.CreditNotesMapping.OutGoingData
{
    public class DisplayCreditNotesProfile : Profile
    {
        public DisplayCreditNotesProfile()
        {
            CreateMap<CreditNote, CreditNoteDto>()
                .ForMember(dest => dest.OriginalInvoiceNumber , opt => opt.MapFrom(src => src.OriginalInvoice.InvoiceNumber));
        }
    }
}
