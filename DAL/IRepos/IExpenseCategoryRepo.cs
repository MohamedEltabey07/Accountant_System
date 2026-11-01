using DAL.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.IRepos
{
    public interface IExpenseCategoryRepo : IGenericRepo<ExpenseCategory>
    {
        Task<List<ExpenseCategory>> GetActiveCategoriesAsync();
        Task<bool> NameExistsAsync(string name, int? excludeId = null);
    }
}
