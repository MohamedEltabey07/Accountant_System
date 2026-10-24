using AutoMapper;
using BLL.DTOs.Vendor.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.VendorMapping.OutGoingData
{
    public class DisplayActiveVendors : Profile
    {
        public DisplayActiveVendors()
        {
            CreateMap<Vendor, VendorDto>().
                ForMember(dest => dest.CurrentBalance, opt => opt.Ignore());
        }
    }
}
