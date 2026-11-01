using AutoMapper;
using BLL.DTOs.MaritimeCompany.InComingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using DAL.Entities;

namespace BLL.Mapping.MaritimeCompanyMapping
{
    public class MaritimeCompanyMapping : Profile
    {
        public MaritimeCompanyMapping()
        {
            CreateMap<CreateMaritimeCompanyDto, Customer>()
                .ForMember(dest => dest.Type, opt => opt.Ignore())
                .ForMember(dest => dest.IsActive, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());

            CreateMap<Customer, MaritimeCompanyDto>()
                .ForMember(dest => dest.OutstandingBalance, opt => opt.Ignore());
        }
    }
}
