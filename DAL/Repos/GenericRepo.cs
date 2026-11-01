using DAL.AppDbContext;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DAL.Repos
{
    public class GenericRepo<T> : IGenericRepo<T> where T : class
    {
        private readonly AppDbcontext _context;
        private readonly DbSet<T> _dbSet;

        public GenericRepo(AppDbcontext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public async Task AddAsync(T item)
        {
            await _context.AddAsync(item);
        }

        public bool Delete(T item)
        {
            _context.Remove(item);
            return true;
        }

        public async Task<bool> FindAsync(T item)
        {
            var checkItem = await _dbSet.FindAsync(item);
            if (checkItem == null) return false;
            return true;
        }

        public async Task<List<T>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        public async Task<T?> GetByIdAsync(string id)
        {
            return await _dbSet.FindAsync(id);
        }

        public async Task<T?> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        public void Update(T item)
        {
            _dbSet.Update(item);
        }
    }
}
