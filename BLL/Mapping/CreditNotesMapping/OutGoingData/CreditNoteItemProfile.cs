using AutoMapper;
using BLL.DTOs.CreditNote.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.CreditNotesMapping.OutGoingData
{
    public class CreditNoteItemProfile : Profile
    {
        public CreditNoteItemProfile()
        {
            CreateMap<CreditNoteItem, CreditNoteItemDto>();
        }
    }
}
