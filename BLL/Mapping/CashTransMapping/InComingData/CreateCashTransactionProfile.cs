using AutoMapper;
using BLL.DTOs.Cash.InComingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.CashTransMapping.InComingData
{
    public class CreateCashTransactionProfile : Profile
    {
        public CreateCashTransactionProfile()
        {
            CreateMap<CreateCashMovementDto, CashTransaction>();
        }
    }
}
