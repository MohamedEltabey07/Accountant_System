using AutoMapper;
using BLL.DTOs.Product.InComingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.ProductMapping.InCommingData
{
    public class UpdateProductMapping : Profile
    {
        public UpdateProductMapping()
        {
            CreateMap<UpdateProductDto, Product>();
        }
    }

}
