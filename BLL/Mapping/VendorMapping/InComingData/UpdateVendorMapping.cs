using AutoMapper;
using BLL.DTOs.Vendor.InComingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.VendorMapping.InComingData
{
    public class UpdateVendorMapping : Profile
    {
        public UpdateVendorMapping()
        {
            CreateMap<UpdateVendorDto, Vendor>();
        }
    }
}
