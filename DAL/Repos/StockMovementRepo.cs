using DAL.AppDbContext;
using DAL.Entities;
using DAL.Enums;
using DAL.IRepos;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DAL.Repos
{
    // The stock ledger repo. Balances are always derived by summing the immutable
    // StockMovement ledger — the movements are the source of truth, never caches.
    public class StockMovementRepo : GenericRepo<StockMovement>, IStockMovementRepo
    {
        private readonly AppDbcontext _context;

        public StockMovementRepo(AppDbcontext context) : base(context)
        {
            _context = context;
        }

        public async Task<List<StockMovement>> GetFilteredAsync(int? productId, StockMovementType? movementType, DateTime? from, DateTime? to)
        {
            var query = _context.StockMovements
                .Include(sm => sm.Product)
                .Include(sm => sm.SourceInvoice)
                .AsQueryable();

            if (productId.HasValue)
                query = query.Where(sm => sm.ProductId == productId.Value);

            if (movementType.HasValue)
                query = query.Where(sm => sm.MovementType == movementType.Value);

            if (from.HasValue)
            {
                var fromDate = from.Value.Date;
                query = query.Where(sm => sm.MovementDate >= fromDate);
            }

            if (to.HasValue)
            {
                var toDateExclusive = to.Value.Date.AddDays(1);
                query = query.Where(sm => sm.MovementDate < toDateExclusive);
            }

            return await query
                .OrderBy(sm => sm.MovementDate)
                .ThenBy(sm => sm.Id)
                .ToListAsync();
        }

        public async Task<List<StockMovement>> GetMovementsForProductAsync(int productId)
        {
            return await _context.StockMovements
                .Include(sm => sm.Product)
                .Include(sm => sm.SourceInvoice)
                .Where(sm => sm.ProductId == productId)
                .OrderBy(sm => sm.MovementDate)
                .ThenBy(sm => sm.Id)
                .ToListAsync();
        }

        public async Task<List<StockMovement>> GetMovementsForInvoiceAsync(int invoiceId)
        {
            return await _context.StockMovements
                .Include(sm => sm.Product)
                .Where(sm => sm.SourceInvoiceId == invoiceId)
                .OrderBy(sm => sm.Id)
                .ToListAsync();
        }

        public async Task<List<StockBalanceSummary>> GetFrozenFishBalancesAsync()
        {
            // Every frozen fish product participates in the report, even with zero stock
            // (needed for "items out of stock" listing). Products NOT in the table are
            // aggregated with the ledger sums by product id.
            var frozenFishProducts = await _context.Products
                .Where(p => p.Category == ProductCategory.FrozenFish)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.DefaultCartonWeightKg,
                    p.MaritimePricePerKg
                })
                .ToListAsync();

            var balances = new List<StockBalanceSummary>();

            if (!frozenFishProducts.Any())
                return balances;

            var productIds = frozenFishProducts.Select(p => p.Id).ToList();
            var aggregates = await _context.StockMovements
                .Where(sm => productIds.Contains(sm.ProductId))
                .GroupBy(sm => sm.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    TotalInCartons = g.Where(sm => sm.Cartons > 0).Sum(sm => (decimal?)sm.Cartons) ?? 0m,
                    TotalInKg = g.Where(sm => sm.TotalWeightKg > 0).Sum(sm => (decimal?)sm.TotalWeightKg) ?? 0m,
                    TotalOutCartons = g.Where(sm => sm.Cartons < 0).Sum(sm => (decimal?)-sm.Cartons) ?? 0m,
                    TotalOutKg = g.Where(sm => sm.TotalWeightKg < 0).Sum(sm => (decimal?)-sm.TotalWeightKg) ?? 0m
                })
                .ToListAsync();

            foreach (var product in frozenFishProducts)
            {
                var agg = aggregates.FirstOrDefault(a => a.ProductId == product.Id);
                var summary = new StockBalanceSummary
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    DefaultCartonWeightKg = product.DefaultCartonWeightKg,
                    MaritimePricePerKg = product.MaritimePricePerKg,
                    TotalInCartons = agg?.TotalInCartons ?? 0m,
                    TotalInKg = agg?.TotalInKg ?? 0m,
                    TotalOutCartons = agg?.TotalOutCartons ?? 0m,
                    TotalOutKg = agg?.TotalOutKg ?? 0m
                };

                // Balance = In - Out, adjusted for (signed) corrections. Mathematically
                // identical to summing (In - Out) - |AdjustmentDecrease| etc.
                summary.AvailableCartons = summary.TotalInCartons - summary.TotalOutCartons;
                summary.AvailableKg = summary.TotalInKg - summary.TotalOutKg;
                balances.Add(summary);
            }

            return balances.OrderBy(b => b.ProductName).ToList();
        }

        public async Task<StockBalanceSummary?> GetBalanceForProductAsync(int productId)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == productId);

            if (product == null)
                return null;

            var totalInCartons = await _context.StockMovements
                .Where(sm => sm.ProductId == productId && sm.Cartons > 0)
                .SumAsync(sm => (decimal?)sm.Cartons) ?? 0m;
            var totalInKg = await _context.StockMovements
                .Where(sm => sm.ProductId == productId && sm.TotalWeightKg > 0)
                .SumAsync(sm => (decimal?)sm.TotalWeightKg) ?? 0m;
            var totalOutCartons = await _context.StockMovements
                .Where(sm => sm.ProductId == productId && sm.Cartons < 0)
                .SumAsync(sm => (decimal?)-sm.Cartons) ?? 0m;
            var totalOutKg = await _context.StockMovements
                .Where(sm => sm.ProductId == productId && sm.TotalWeightKg < 0)
                .SumAsync(sm => (decimal?)-sm.TotalWeightKg) ?? 0m;

            return new StockBalanceSummary
            {
                ProductId = product.Id,
                ProductName = product.Name,
                DefaultCartonWeightKg = product.DefaultCartonWeightKg,
                MaritimePricePerKg = product.MaritimePricePerKg,
                TotalInCartons = totalInCartons,
                TotalInKg = totalInKg,
                TotalOutCartons = totalOutCartons,
                TotalOutKg = totalOutKg,
                AvailableCartons = totalInCartons - totalOutCartons,
                AvailableKg = totalInKg - totalOutKg
            };
        }

        public async Task<decimal> GetAvailableWeightKgAsync(int productId)
        {
            var available = await _context.StockMovements
                .Where(sm => sm.ProductId == productId)
                .SumAsync(sm => (decimal?)sm.TotalWeightKg) ?? 0m;

            return available;
        }
    }
}
