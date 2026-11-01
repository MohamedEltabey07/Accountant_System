using AutoMapper;
using BLL.DTOs.Product.InComingData;
using BLL.DTOs.Product.OutGoingData;
using BLL.IService;
using DAL.Entities;
using DAL.Enums;
using DAL.IConfiguration;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Service
{
    public class ProductService : IProductService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public ProductService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<bool> CreateProductAsync(CreateProductDto dto)
        {
            if (dto == null) return false;
            ValidateFrozenFish(dto.Category, dto.DefaultCartonWeightKg);
            var mapToProduct = _mapper.Map<Product>(dto);
            await _unitOfWork.ProductRepo.AddAsync(mapToProduct);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        private static void ValidateFrozenFish(ProductCategory category, decimal? cartonWeightKg)
        {
            if (category == ProductCategory.FrozenFish)
            {
                if (cartonWeightKg is null || cartonWeightKg <= 0)
                    throw new InvalidOperationException("الأصناف المجمدة تتطلب تحديد وزن الكرتونة الافتراضي بالكجم.");
            }
            else
            {
                if (cartonWeightKg.HasValue && cartonWeightKg.Value > 0)
                    throw new InvalidOperationException("وزن الكرتونة يستخدم لأصناف الأسماك المجمدة فقط.");
            }
        }

        public async Task<bool> DeleteProductAsync(int id)
        {
            var getProduct = await _unitOfWork.ProductRepo.GetByIdAsync(id);
            if (getProduct == null) return false;
            getProduct.IsActive = false;
            _unitOfWork.ProductRepo.Update(getProduct);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<List<ProductDto>> GetActiveProductsAsync()
        {
            var listOfAvtiveProducts = await _unitOfWork.ProductRepo.GetActiveProductsAsync();
            var listOfAvtiveProductsDto = _mapper.Map<List<ProductDto>>(listOfAvtiveProducts);
            return listOfAvtiveProductsDto;
        }

        public async Task<List<ProductDto>> GetProductsByCategoryAsync(ProductCategory category, bool activeOnly = true)
        {
            var products = activeOnly
                ? await _unitOfWork.ProductRepo.GetByCategoryAsync(category)
                : (await _unitOfWork.ProductRepo.GetAllAsync()).Where(p => p.Category == category).ToList();
            return _mapper.Map<List<ProductDto>>(products);
        }

        public async Task<List<ProductDto>> GetAllProductsAsync()
        {
            var listOfAvtiveProducts = await _unitOfWork.ProductRepo.GetAllAsync();
            var listOfAvtiveProductsDto = _mapper.Map<List<ProductDto>>(listOfAvtiveProducts);
            return listOfAvtiveProductsDto;
        }

        public async Task<ProductDto?> GetProductByIdAsync(int id)
        {
            var product = await _unitOfWork.ProductRepo.GetByIdAsync(id);
            if (product == null) return null;
            var productDto = _mapper.Map<ProductDto>(product);
            return productDto;
        }

        public async Task<bool> ToggleStatusAsync(int id)
        {
            var product =await _unitOfWork.ProductRepo.GetByIdAsync(id);
            if (product == null) return false;
            product.IsActive = !product.IsActive;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateProductAsync(UpdateProductDto dto)
        {
            if (dto == null) return false;
            var existanceProduct = await _unitOfWork.ProductRepo.GetByIdAsync(dto.Id);
            if (existanceProduct == null) return false;
            ValidateFrozenFish(dto.Category, dto.DefaultCartonWeightKg);
            _mapper.Map(dto, existanceProduct);
            existanceProduct.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
    }
}
