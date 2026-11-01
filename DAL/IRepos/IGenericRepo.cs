using Microsoft.EntityFrameworkCore.Update.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.IRepos
{
    public interface IGenericRepo<T> where T : class
    {
        public Task AddAsync (T item);
        public void Update(T item);
        public bool Delete(T item);
        public Task<T?> GetByIdAsync(string id);
        public Task<T?> GetByIdAsync(int id);
        public Task<List<T>> GetAllAsync();
        public Task<bool> FindAsync(T id);

    }
}
