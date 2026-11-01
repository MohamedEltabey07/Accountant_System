using AutoMapper;
using BLL.DTOs.OpeningBalance.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.OpeningBalanceMapping.OutGoingData
{
    public class DisplayOpeningBalanceProfile : Profile
    {
        public DisplayOpeningBalanceProfile()
        {
            CreateMap<OpeningBalanceEntry, OpeningBalanceDto>();
        }
    }
}
