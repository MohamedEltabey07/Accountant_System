using AutoMapper;
using BLL.DTOs.Product.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.ProductMapping.OutGoingData
{
    public class DisplayActiveProducts : Profile
    {
        public DisplayActiveProducts()
        {
            CreateMap<Product, ProductDto>();
        }

    }
}
