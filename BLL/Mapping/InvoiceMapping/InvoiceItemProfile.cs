using AutoMapper;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Invoice.OutGoingData;
using DAL.Entities;

namespace BLL.Mapping.InvoiceMapping
{
    public class InvoiceItemProfile : Profile
    {
        public InvoiceItemProfile()
        {
            CreateMap<InvoiceItem, InvoiceItemDto>();
            CreateMap<InvoiceItemDto, InvoiceItem>();
            CreateMap<AddInvoiceItemDto, InvoiceItem>();
        }
    }
}
