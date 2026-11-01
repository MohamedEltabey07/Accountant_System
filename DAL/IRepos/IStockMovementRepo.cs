using DAL.Entities;
using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DAL.IRepos
{
    public interface IStockMovementRepo : IGenericRepo<StockMovement>
    {
        public Task<List<StockMovement>> GetFilteredAsync(int? productId, StockMovementType? movementType, DateTime? from, DateTime? to);

        public Task<List<StockMovement>> GetMovementsForProductAsync(int productId);

        public Task<List<StockMovement>> GetMovementsForInvoiceAsync(int invoiceId);

        public Task<List<StockBalanceSummary>> GetFrozenFishBalancesAsync();

        public Task<StockBalanceSummary?> GetBalanceForProductAsync(int productId);

        public Task<decimal> GetAvailableWeightKgAsync(int productId);
    }
}
