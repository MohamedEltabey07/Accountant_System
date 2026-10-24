using AutoMapper;
using BLL.DTOs.Customer.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.CustomerMapping.OutGoingData
{
    public class DisplayCustomerMapping : Profile
    {
        public DisplayCustomerMapping()
        {
            CreateMap<Customer, CustomerDto>().
                ForMember(dest => dest.CurrentBalance, opt => opt.Ignore());
        }
    }
}
