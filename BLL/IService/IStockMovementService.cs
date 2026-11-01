using BLL.DTOs.Inventory.InComingData;
using BLL.DTOs.Inventory.OutGoingData;
using BLL.DTOs.Product.OutGoingData;
using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BLL.IService
{
    public interface IStockMovementService
    {
        public Task<List<StockBalanceDto>> GetStockBalancesAsync();

        public Task<StockBalanceDto?> GetBalanceAsync(int productId);

        public Task<InventoryDashboardDto> GetInventoryDashboardAsync(DateTime? from, DateTime? to);

        public Task<List<StockMovementDto>> GetMovementsAsync(int? productId, StockMovementType? movementType, DateTime? from, DateTime? to);

        public Task<List<ProductDto>> GetFrozenFishProductsAsync();

        public Task RecordStockInAsync(CreateStockInDto dto, string? userId = null);

        public Task RecordStockOutAsync(CreateStockOutDto dto, string? userId = null);

        public Task RecordAdjustmentAsync(CreateStockAdjustmentDto dto, string? userId = null);
    }
}
