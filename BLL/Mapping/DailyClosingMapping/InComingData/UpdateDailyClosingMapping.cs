using AutoMapper;
using BLL.DTOs.DailyClosing.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.DailyClosingMapping.InComingData
{
    public class UpdateDailyClosingMapping : Profile
    {
        public UpdateDailyClosingMapping()
        {
            CreateMap<DailyClosingDto, DailyClosing>();
        }
    }
}
