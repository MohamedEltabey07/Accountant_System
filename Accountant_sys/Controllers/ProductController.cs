using Accountant_sys.Models;
using BLL.DTOs.Product.InComingData;
using BLL.DTOs.Product.OutGoingData;
using BLL.IService;
using Microsoft.AspNetCore.Mvc;

namespace Accountant_sys.Controllers
{
    public class ProductController : Controller
    {
        private readonly IProductService _productService;

        public ProductController(IProductService productService)
        {
            _productService = productService;
        }

        // ==========================================
        // LIST
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(string? status)
        {
            // Operational listing defaults to ACTIVE products only; the status
            // filter (active/inactive/all) covers management and re-activation.
            var filter = ActiveStatusFilter.ToIsActive(status);
            ViewBag.Status = string.IsNullOrWhiteSpace(status) ? "active" : ActiveStatusFilter.Normalize(status);
            var products = await _productService.GetProductsByActiveStatusAsync(filter) ?? new List<ProductDto>();
            return View(products);
        }

        // ==========================================
        // DETAILS
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();

            return View(product);
        }

        // ==========================================
        // CREATE
        // ==========================================
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateProductDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                var result = await _productService.CreateProductAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم إضافة المنتج بنجاح.";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(dto);
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء إضافة المنتج. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // EDIT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();

            var dto = new UpdateProductDto
            {
                Id = product.Id,
                Name = product.Name,
                PurchasePrice = product.PurchasePrice,
                WholesalePrice = product.WholesalePrice,
                RetailPrice = product.RetailPrice,
                IsActive = product.IsActive,
                Category = product.Category,
                DefaultCartonWeightKg = product.DefaultCartonWeightKg,
                MaritimePricePerKg = product.MaritimePricePerKg
            };

            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateProductDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                var result = await _productService.UpdateProductAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم تحديث المنتج بنجاح.";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(dto);
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء تحديث المنتج. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // TOGGLE STATUS (Activate / Deactivate)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var result = await _productService.ToggleStatusAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم تغيير حالة المنتج بنجاح.";
            else
                TempData["ErrorMessage"] = "المنتج غير موجود.";

            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // DELETE (Soft delete via service)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _productService.DeleteProductAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم إلغاء تنشيط المنتج بنجاح.";
            else
                TempData["ErrorMessage"] = "المنتج غير موجود.";

            return RedirectToAction(nameof(Index));
        }
    }
}
