using AutoMapper;
using BLL.DTOs.ExpenseCategory.OutGoingData;
using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Mapping.ExpenseCategoryMapping.OutGoingData
{
    public class DisplayExpenseCategoryProfile : Profile
    {
        public DisplayExpenseCategoryProfile()
        {
            CreateMap<ExpenseCategory, ExpenseCategoryDto>();
        }
    }
}
