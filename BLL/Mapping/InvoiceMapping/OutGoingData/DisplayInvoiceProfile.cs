using AutoMapper;
using BLL.DTOs.Invoice.OutGoingData;
using DAL.Entities;

namespace BLL.Mapping.InvoiceMapping.OutGoingData
{
    public class DisplayInvoiceProfile : Profile
    {
        public DisplayInvoiceProfile()
        {
            CreateMap<Invoice, InvoiceDto>()
                .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null))
                .ForMember(dest => dest.VendorName, opt => opt.MapFrom(src => src.Vendor != null ? src.Vendor.Name : null))
                .ForMember(dest => dest.RemainingBalance, opt => opt.Ignore())
                .ForMember(dest => dest.PaymentStatus, opt => opt.Ignore());
        }
    }
}
