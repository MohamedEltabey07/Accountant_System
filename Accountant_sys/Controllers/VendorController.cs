using BLL.DTOs.Vendor.InComingData;
using BLL.DTOs.Vendor.OutGoingData;
using BLL.IService;
using Microsoft.AspNetCore.Mvc;

namespace Accountant_sys.Controllers
{
    public class VendorController : Controller
    {
        private readonly IVendorService _vendorService;

        public VendorController(IVendorService vendorService)
        {
            _vendorService = vendorService;
        }

        // ==========================================
        // LIST
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var vendors = await _vendorService.GetAllVendorAsync() ?? new List<VendorDto>();
            return View(vendors);
        }

        // ==========================================
        // DETAILS (with balance)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var vendor = await _vendorService.GetVendorByIdAsync(id);
            if (vendor == null) return NotFound();

            return View(vendor);
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
        public async Task<IActionResult> Create(CreateVendorDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _vendorService.CreateVendorAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم إضافة المورد بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء إضافة المورد. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // EDIT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var vendor = await _vendorService.GetVendorByIdAsync(id);
            if (vendor == null) return NotFound();

            var dto = new UpdateVendorDto
            {
                Id = vendor.Id,
                Name = vendor.Name,
                PhoneNumber = vendor.PhoneNumber,
                Address = vendor.Address,
                IsActive = vendor.IsActive
            };

            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateVendorDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _vendorService.UpdateVendorAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم تحديث بيانات المورد بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء تحديث بيانات المورد. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // TOGGLE STATUS (Activate / Deactivate)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var result = await _vendorService.ToggleStatusAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم تغيير حالة المورد بنجاح.";
            else
                TempData["ErrorMessage"] = "المورد غير موجود.";

            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // DELETE (Soft delete via service)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _vendorService.DeleteVendorAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم إلغاء تنشيط المورد بنجاح.";
            else
                TempData["ErrorMessage"] = "المورد غير موجود.";

            return RedirectToAction(nameof(Index));
        }
    }
}
