using AutoMapper;
using BLL.DTOs.Auth;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.AdminMapping.InCommingData
{
    public class RegisterAdminProfile : Profile
    {
        public RegisterAdminProfile()
        {
            CreateMap<RegisterAdminDto, AppUser>()
                .ForMember(dest => dest.FullName, opt => opt.MapFrom(src =>  src.FirstName + " " + src.LastName ));
        }
    }
}
