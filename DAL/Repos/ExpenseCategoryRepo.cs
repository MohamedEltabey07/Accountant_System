using DAL.AppDbContext;
using DAL.Entities;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.Repos
{
    public class ExpenseCategoryRepo : GenericRepo<ExpenseCategory>, IExpenseCategoryRepo
    {
        private readonly AppDbcontext _context;

        public ExpenseCategoryRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<List<ExpenseCategory>> GetActiveCategoriesAsync()
        {
            var activeCategories = await _context.ExpenseCategories.Where(i => i.IsActive).ToListAsync();
            return activeCategories;
        }

        public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
        {
            var categoryName = await _context.ExpenseCategories.AnyAsync(n => n.Name == name
            && (!excludeId.HasValue || n.Id != excludeId));
            return categoryName;
        }
    }
}
