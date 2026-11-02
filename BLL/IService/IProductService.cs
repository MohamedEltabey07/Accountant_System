using BLL.DTOs.Product.InComingData;
using BLL.DTOs.Product.OutGoingData;
using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.IService
{
    public interface IProductService
    {
        public Task<List<ProductDto>> GetAllProductsAsync();
        public Task<List<ProductDto>> GetActiveProductsAsync();
        // isActive: true = active only (default operational view),
        // false = inactive only, null = all records (management view).
        public Task<List<ProductDto>> GetProductsByActiveStatusAsync(bool? isActive);
        public Task<List<ProductDto>> GetProductsByCategoryAsync(ProductCategory category, bool activeOnly = true);
        public Task<ProductDto?> GetProductByIdAsync(int id);
        public Task<bool> UpdateProductAsync(UpdateProductDto dto);
        public Task<bool> ToggleStatusAsync(int id);
        public Task<bool> DeleteProductAsync(int id);
        Task<bool> CreateProductAsync(CreateProductDto dto);
    }
}
