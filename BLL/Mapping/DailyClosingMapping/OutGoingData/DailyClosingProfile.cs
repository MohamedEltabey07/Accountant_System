using AutoMapper;
using BLL.DTOs.DailyClosing.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.DailyClosingMapping.OutGoingData
{
    public class DailyClosingProfile : Profile
    {
        public DailyClosingProfile()
        {
            CreateMap<DailyClosing, DailyClosingDto>();
        }
    }
}
