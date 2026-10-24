using DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.IRepos
{
    public interface IProductRepo : IGenericRepo<Product>
    {
        public Task<List<Product>> GetActiveProductsAsync();

        public Task<bool> IsProductUsedInInvoicesAsync(int productId);

        public Task<Product?> GetByNameAsync(string name);
    }
}
