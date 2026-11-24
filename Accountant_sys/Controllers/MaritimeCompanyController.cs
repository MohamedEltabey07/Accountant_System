using Accountant_sys.Models;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.MaritimeCompany.InComingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    // Maritime companies management — lives on top of the shared customer
    // accounting stack but stays a completely separate business screen.
    public class MaritimeCompanyController : Controller
    {
        private readonly IMaritimeCompanyService _maritimeCompanyService;
        private readonly IInvoiceService _invoiceService;
        private readonly ICashTransactionService _cashTransactionService;
        private readonly ICreditNoteService _creditNoteService;
        private readonly IOpeningBalanceService _openingBalanceService;

        public MaritimeCompanyController(
            IMaritimeCompanyService maritimeCompanyService,
            IInvoiceService invoiceService,
            ICashTransactionService cashTransactionService,
            ICreditNoteService creditNoteService,
            IOpeningBalanceService openingBalanceService)
        {
            _maritimeCompanyService = maritimeCompanyService;
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
            // Operational listing defaults to ACTIVE maritime companies only;
            // the status filter (active/inactive/all) covers management.
            var filter = ActiveStatusFilter.ToIsActive(status);
            ViewBag.Status = string.IsNullOrWhiteSpace(status) ? "active" : ActiveStatusFilter.Normalize(status);
            var companies = await _maritimeCompanyService.GetCompaniesByActiveStatusAsync(filter)
                ?? new List<MaritimeCompanyDto>();
            return View(companies);
        }

        // ==========================================
        // DETAILS (balance + supply orders)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var company = await _maritimeCompanyService.GetCompanyByIdAsync(id);
            if (company == null) return NotFound();

            var allSupplyOrders = await _invoiceService.GetInvoicesAsync(InvoiceType.MaritimeSupply, null)
                ?? new List<InvoiceDto>();
            ViewBag.SupplyOrders = allSupplyOrders.Where(i => i.CustomerId == id)
                .OrderByDescending(i => i.TransactionDate)
                .ToList();

            return View(company);
        }

        // ==========================================
        // ACCOUNT STATEMENT
        // mode: "summary"  = summary ledger (each supply order one row,
        //                     carrying its EXTERNAL order number).
        //       "detailed" = detailed supply orders statement (per-order
        //                     header + all items + prominent order total).
        // Both modes render from the same ledger lines, so financial totals
        // are identical for identical filters.
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Statement(int? id, DateTime? from, DateTime? to, string? mode)
        {
            var activeCompanies = await _maritimeCompanyService.GetActiveCompaniesAsync() ?? new List<MaritimeCompanyDto>();
            ViewBag.Parties = new SelectList(activeCompanies, "Id", "Name");

            // Detailed is the reference default; "summary" stays available
            // through the on-screen toggle and the URL.
            var viewMode = string.Equals(mode, "summary", StringComparison.OrdinalIgnoreCase)
                ? "summary"
                : "detailed";
            ViewBag.Mode = viewMode;

            if (id == null || id <= 0)
                return View();

            var company = activeCompanies.FirstOrDefault(c => c.Id == id)
                ?? (await _maritimeCompanyService.GetAllCompaniesAsync() ?? new List<MaritimeCompanyDto>()).FirstOrDefault(c => c.Id == id);
            if (company == null) return NotFound();

            ViewBag.SelectedCompanyId = company.Id;

            var model = await StatementPageDataLoader.LoadAsync(
                StatementAccountType.MaritimeCompany, company.Id, company.Name, "MaritimeCompany", from, to, viewMode,
                company.PhoneNumber, company.Address,
                _openingBalanceService, _invoiceService, _creditNoteService, _cashTransactionService);

            ViewBag.SupplyOrders = model.SupplyOrders;
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
        public async Task<IActionResult> Create(CreateMaritimeCompanyDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                var result = await _maritimeCompanyService.CreateCompanyAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم إنشاء الشركة البحرية بنجاح.";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(dto);
            }

            ModelState.AddModelError(string.Empty, "تعذّر إنشاء الشركة البحرية. تحقق من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // EDIT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var company = await _maritimeCompanyService.GetCompanyByIdAsync(id);
            if (company == null) return NotFound();

            var dto = new UpdateMaritimeCompanyDto
            {
                Id = company.Id,
                Name = company.Name,
                PhoneNumber = company.PhoneNumber,
                Address = company.Address,
                IsActive = company.IsActive
            };

            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateMaritimeCompanyDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                var result = await _maritimeCompanyService.UpdateCompanyAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم تعديل بيانات الشركة البحرية بنجاح.";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(dto);
            }

            ModelState.AddModelError(string.Empty, "تعذّر تعديل بيانات الشركة البحرية. حاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // TOGGLE STATUS (activate / deactivate)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var result = await _maritimeCompanyService.ToggleStatusAsync(id);
            TempData[result ? "SuccessMessage" : "ErrorMessage"] = result
                ? "تم تغيير حالة الشركة البحرية بنجاح."
                : "تعذر تغيير حالة الشركة.";

            return RedirectToAction(nameof(Index));
        }
    }
}
