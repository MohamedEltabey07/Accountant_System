using BLL.DTOs.ExpenseCategory.InComingData;
using BLL.DTOs.ExpenseCategory.OutGoingData;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface IExpenseCategoryService
    {
        Task<List<ExpenseCategoryDto>> GetAllCategoriesAsync();
        Task<List<ExpenseCategoryDto>> GetActiveCategoriesAsync();
        // isActive: true = active only (default operational view),
        // false = inactive only, null = all records (management view).
        Task<List<ExpenseCategoryDto>> GetCategoriesByActiveStatusAsync(bool? isActive);
        Task<bool> CreateCategoryAsync(CreateExpenseCategoryDto dto);
        Task<bool> UpdateCategoryAsync(UpdateExpenseCategoryDto dto);
        Task<bool> ToggleCategoryStatusAsync(int id);
    }
}
