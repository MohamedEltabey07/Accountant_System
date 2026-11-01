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
            CreateMap<InvoiceItem, InvoiceItemDto>()
                .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src =>
                    !string.IsNullOrWhiteSpace(src.ProductName)
                        ? src.ProductName
                        : (src.Product != null && !string.IsNullOrWhiteSpace(src.Product.Name) ? src.Product.Name : "صنف غير محدد")));

            CreateMap<InvoiceItemDto, InvoiceItem>();
            CreateMap<AddInvoiceItemDto, InvoiceItem>();
        }
    }
}
