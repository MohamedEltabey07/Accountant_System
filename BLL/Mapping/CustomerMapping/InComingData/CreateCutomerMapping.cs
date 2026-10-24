using AutoMapper;
using BLL.DTOs.Customer.InComingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.CustomerMapping.InComingData
{
    public class CreateCutomerMapping : Profile
    {
        public CreateCutomerMapping()
        {
            CreateMap<CreateCustomerDto, Customer>();
        }
    }
}
