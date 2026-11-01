using AutoMapper;
using BLL.DTOs.ExpenseCategory.InComingData;
using BLL.DTOs.ExpenseCategory.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.Service
{
    public class ExpenseCategoryService : IExpenseCategoryService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public ExpenseCategoryService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<List<ExpenseCategoryDto>> GetAllCategoriesAsync()
        {
            var allCategoriesList = await _unitOfWork.ExpenseCategoryRepo.GetAllAsync();
            var mappedExpenseCatList = _mapper.Map<List<ExpenseCategoryDto>>(allCategoriesList);
            return mappedExpenseCatList;

        }

        public async Task<List<ExpenseCategoryDto>> GetActiveCategoriesAsync()
        {
            var allCategoriesList = await _unitOfWork.ExpenseCategoryRepo.GetActiveCategoriesAsync();
            var mappedExpenseCatList = _mapper.Map<List<ExpenseCategoryDto>>(allCategoriesList);
            return mappedExpenseCatList;
        }

        public async Task<bool> CreateCategoryAsync(CreateExpenseCategoryDto dto)
        {
            if (dto == null) throw new NullReferenceException("يجب عليك ادخال نوع المصروفات");
            var checkNameExist = await _unitOfWork.ExpenseCategoryRepo.NameExistsAsync(dto.Name);
            if (checkNameExist) throw new InvalidOperationException("اسم هذاالمصروف موجود بالفعل");
            var expenseCat = new ExpenseCategory { Name = dto.Name, IsActive = true , CreatedAt = DateTime.UtcNow };
            await _unitOfWork.ExpenseCategoryRepo.AddAsync(expenseCat);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateCategoryAsync(UpdateExpenseCategoryDto dto)
        {
            if (dto.Id == 0) throw new InvalidOperationException("هذا النوع غير موجود في جدول المصروفات");
            var checkNameExist = await _unitOfWork.ExpenseCategoryRepo.NameExistsAsync(dto.Name,dto.Id);
            var existCat = await _unitOfWork.ExpenseCategoryRepo.GetByIdAsync(dto.Id);
            existCat.Name = dto.Name;
            existCat.IsActive = dto.IsActive;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ToggleCategoryStatusAsync(int id)
        {
            if (id == 0) throw new ArgumentNullException("هذا النوع غير موجود في جدول المصروفات");
            var existCat = await _unitOfWork.ExpenseCategoryRepo.GetByIdAsync(id);
            existCat.IsActive = !existCat.IsActive;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
    }
}
