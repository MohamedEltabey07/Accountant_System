using DAL.AppDbContext;
using DAL.Entities;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.Repos
{
    public class ProductRepo : GenericRepo<Product>, IProductRepo
    {
        private readonly AppDbcontext _context;

        public ProductRepo(AppDbcontext context):base(context) 
        {
            _context = context;
        }

        public async Task<List<Product>> GetActiveProductsAsync()
        {
            var listOfActiveProducts = await _context.Products.Where(a => a.IsActive).ToListAsync();
            return listOfActiveProducts;
        }

        public async Task<Product?> GetByNameAsync(string name)
        {
            var product = await _context.Products.FirstOrDefaultAsync(  a => a.Name == name);
            return product;
        }

        public async Task<bool> IsProductUsedInInvoicesAsync(int productId)
        {
            var checkProductInInvoices =await _context.InvoiceItems.Where(p => p.ProductId == productId).AnyAsync();
            if (checkProductInInvoices) return true;
            return false;
        }
    }
}
