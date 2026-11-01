using AutoMapper;
using BLL.DTOs.OpeningBalance.InComingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.OpeningBalanceMapping.InComingData
{
    public class CreateOpeningBalanceProfile : Profile
    {
        public CreateOpeningBalanceProfile()
        {
            CreateMap<CreateOpeningBalanceDto, OpeningBalanceEntry>();
        }
    }
}
