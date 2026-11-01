using BLL.DTOs.Inventory.InComingData;
using BLL.DTOs.Inventory.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Security.Claims;

namespace Accountant_sys.Controllers
{
    // Frozen fish freezer inventory: dashboard, manual stock operations and reports.
    public class InventoryController : Controller
    {
        private readonly IStockMovementService _stockMovementService;

        public InventoryController(IStockMovementService stockMovementService)
        {
            _stockMovementService = stockMovementService;
        }

        // ==========================================
        // DASHBOARD (combined balance of both freezers)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to)
        {
            if (from.HasValue && to.HasValue && from > to)
            {
                TempData["ErrorMessage"] = "نطاق التاريخ غير صحيح: تاريخ البداية بعد تاريخ النهاية.";
                from = null;
                to = null;
            }

            var dashboard = await _stockMovementService.GetInventoryDashboardAsync(from, to);
            return View(dashboard);
        }

        // ==========================================
        // MOVEMENT HISTORY (filterable)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Movements(int? productId, StockMovementType? movementType, DateTime? from, DateTime? to)
        {
            if (from.HasValue && to.HasValue && from > to)
            {
                TempData["ErrorMessage"] = "نطاق التاريخ غير صحيح: تاريخ البداية بعد تاريخ النهاية.";
                from = null;
                to = null;
            }

            var movements = await _stockMovementService.GetMovementsAsync(productId, movementType, from, to);
            var fishProducts = await _stockMovementService.GetFrozenFishProductsAsync();

            ViewBag.Products = new SelectList(fishProducts, "Id", "Name", productId);
            ViewBag.SelectedProductId = productId;
            ViewBag.SelectedType = movementType;
            ViewBag.FromDate = from;
            ViewBag.ToDate = to;

            return View(movements);
        }

        // ==========================================
        // STOCK RECEIPT (كارتونات داخلة للثلاجات)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> StockIn()
        {
            await PrepareFishProductsAsync();
            return View(new CreateStockInDto { MovementDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StockIn(CreateStockInDto dto)
        {
            if (!ModelState.IsValid)
            {
                await PrepareFishProductsAsync();
                return View(dto);
            }

            try
            {
                await _stockMovementService.RecordStockInAsync(dto, CurrentUserId());
                TempData["SuccessMessage"] = $"تم تسجيل استلام {dto.Cartons:N2} كرتونة ({(dto.Cartons * (dto.WeightPerCartonKg ?? 0)):N3} كجم) في مخزون الثلاجات.";
                return RedirectToAction(nameof(Movements));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PrepareFishProductsAsync();
                return View(dto);
            }
        }

        // ==========================================
        // STOCK WITHDRAWAL (خروج فعلي من الثلاجات)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> StockOut()
        {
            await PrepareFishProductsWithStockAsync();
            return View(new CreateStockOutDto { MovementDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StockOut(CreateStockOutDto dto)
        {
            if (!ModelState.IsValid || dto.WeightKg <= 0)
            {
                if (dto.WeightKg <= 0)
                    ModelState.AddModelError(string.Empty, "وزن الخروج (كجم) يجب أن يكون أكبر من صفر.");

                await PrepareFishProductsWithStockAsync();
                return View(dto);
            }

            try
            {
                await _stockMovementService.RecordStockOutAsync(dto, CurrentUserId());
                TempData["SuccessMessage"] = $"تم تسجيل خروج {dto.WeightKg:N3} كجم من مخزون الثلاجات.";
                return RedirectToAction(nameof(Movements));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PrepareFishProductsWithStockAsync();
                return View(dto);
            }
        }

        // ==========================================
        // STOCK ADJUSTMENT (تسوية الجرد الفعلي)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Adjustment()
        {
            await PrepareFishProductsWithStockAsync();
            return View(new CreateStockAdjustmentDto { MovementDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Adjustment(CreateStockAdjustmentDto dto)
        {
            if (!ModelState.IsValid)
            {
                await PrepareFishProductsWithStockAsync();
                return View(dto);
            }

            try
            {
                await _stockMovementService.RecordAdjustmentAsync(dto, CurrentUserId());
                TempData["SuccessMessage"] = "تم تسجيل تسوية المخزون بنجاح.";
                return RedirectToAction(nameof(Movements));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PrepareFishProductsWithStockAsync();
                return View(dto);
            }
        }

        // ==========================================
        // CURRENT STOCK REPORT (printable)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> StockReport()
        {
            var balances = await _stockMovementService.GetStockBalancesAsync();
            return View(balances);
        }

        // ==========================================
        // HELPERS
        // ==========================================
        private async Task PrepareFishProductsAsync()
        {
            var fishProducts = await _stockMovementService.GetFrozenFishProductsAsync() ?? new();
            ViewBag.Products = new SelectList(fishProducts, "Id", "Name");
            ViewBag.FishProducts = fishProducts;
        }

        private async Task PrepareFishProductsWithStockAsync()
        {
            var balances = await _stockMovementService.GetStockBalancesAsync() ?? new();
            ViewBag.Products = new SelectList(
                balances.Where(b => b.IsActive), "ProductId", "ProductName");
            ViewBag.Balances = balances.ToDictionary(b => b.ProductId, b => b);
        }

        private string? CurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
