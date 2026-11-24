using Accountant_sys.Models;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.Vendor.InComingData;
using BLL.DTOs.Vendor.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class VendorController : Controller
    {
        private readonly IVendorService _vendorService;
        private readonly IInvoiceService _invoiceService;
        private readonly ICashTransactionService _cashTransactionService;
        private readonly ICreditNoteService _creditNoteService;
        private readonly IOpeningBalanceService _openingBalanceService;

        public VendorController(
            IVendorService vendorService,
            IInvoiceService invoiceService,
            ICashTransactionService cashTransactionService,
            ICreditNoteService creditNoteService,
            IOpeningBalanceService openingBalanceService)
        {
            _vendorService = vendorService;
            _invoiceService = invoiceService;
            _cashTransactionService = cashTransactionService;
            _creditNoteService = creditNoteService;
            _openingBalanceService = openingBalanceService;
        }

        // ==========================================
        // LIST
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(string? status)
        {
            // Operational listing defaults to ACTIVE vendors only; the status
            // filter (active/inactive/all) covers management.
            var filter = ActiveStatusFilter.ToIsActive(status);
            ViewBag.Status = string.IsNullOrWhiteSpace(status) ? "active" : ActiveStatusFilter.Normalize(status);
            var vendors = await _vendorService.GetVendorsByActiveStatusAsync(filter) ?? new List<VendorDto>();
            return View(vendors);
        }

        // ==========================================
        // DETAILS (with balance & cash flow summary)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var vendor = await _vendorService.GetVendorByIdAsync(id);
            if (vendor == null) return NotFound();

            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                StatementAccountType.Vendor, id, _openingBalanceService, _invoiceService, _creditNoteService, _cashTransactionService);
            ViewBag.StatementSummary = StatementBuilder.Build(
                ledger, isVendor: true, vendor.Id, vendor.Name, "Vendor", null, null,
                StatementAccountType.Vendor, vendor.PhoneNumber, vendor.Address);
            return View(vendor);
        }

        // ==========================================
        // ACCOUNT STATEMENT (كشف حساب مورد)
        // mode: "summary"  = ledger summary rows
        //       "detailed" = per-invoice breakdown (items, prices, discounts, totals) + full ledger
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Statement(int? id, DateTime? from, DateTime? to, string? mode)
        {
            var activeVendors = await _vendorService.GetActiveVendorAsync() ?? new List<VendorDto>();
            ViewBag.Parties = new SelectList(activeVendors, "Id", "Name");

            // Detailed is the reference default; "summary" stays available
            // through the on-screen toggle and the URL.
            var viewMode = string.Equals(mode, "summary", StringComparison.OrdinalIgnoreCase)
                ? "summary"
                : "detailed";
            ViewBag.Mode = viewMode;

            if (id == null || id <= 0)
                return View();

            var vendor = activeVendors.FirstOrDefault(v => v.Id == id)
                ?? (await _vendorService.GetAllVendorAsync() ?? new List<VendorDto>()).FirstOrDefault(v => v.Id == id);
            if (vendor == null) return NotFound();

            var model = await StatementPageDataLoader.LoadAsync(
                StatementAccountType.Vendor, vendor.Id, vendor.Name, "Vendor", from, to, viewMode,
                vendor.PhoneNumber, vendor.Address,
                _openingBalanceService, _invoiceService, _creditNoteService, _cashTransactionService);

            ViewBag.DetailedInvoices = model.DetailedInvoices;
            return View(model);
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
