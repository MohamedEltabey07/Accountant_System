using Accountant_sys.Models;
using BLL.DTOs.Customer.InComingData;
using BLL.DTOs.Customer.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class CustomerController : Controller
    {
        private readonly ICustomerService _customerService;
        private readonly IInvoiceService _invoiceService;
        private readonly ICashTransactionService _cashTransactionService;
        private readonly ICreditNoteService _creditNoteService;
        private readonly IOpeningBalanceService _openingBalanceService;

        public CustomerController(
            ICustomerService customerService,
            IInvoiceService invoiceService,
            ICashTransactionService cashTransactionService,
            ICreditNoteService creditNoteService,
            IOpeningBalanceService openingBalanceService)
        {
            _customerService = customerService;
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
            // Operational listing defaults to ACTIVE wholesale customers only;
            // the status filter (active/inactive/all) covers management.
            var filter = ActiveStatusFilter.ToIsActive(status);
            ViewBag.Status = string.IsNullOrWhiteSpace(status) ? "active" : ActiveStatusFilter.Normalize(status);
            var customers = await _customerService.GetCustomersByActiveStatusAsync(filter) ?? new List<CustomerDto>();
            return View(customers);
        }

        // ==========================================
        // DETAILS (with balance & cash flow summary)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var customer = await _customerService.GetCustomerByIdAsync(id);
            if (customer == null) return NotFound();

            var ledger = await StatementLedgerLoader.LoadPartyLedgerAsync(
                StatementAccountType.Customer, id, _openingBalanceService, _invoiceService, _creditNoteService, _cashTransactionService);
            ViewBag.StatementSummary = StatementBuilder.Build(
                ledger, isVendor: false, customer.Id, customer.Name, "Customer", null, null,
                StatementAccountType.Customer, customer.PhoneNumber, customer.Address);
            return View(customer);
        }

        // ==========================================
        // ACCOUNT STATEMENT (كشف حساب عميل)
        // mode: "summary"  = ledger summary rows
        //       "detailed" = per-invoice breakdown (items, prices, discounts, totals) + full ledger
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Statement(int? id, DateTime? from, DateTime? to, string? mode)
        {
            var activeCustomers = await _customerService.GetActiveCustomersAsync() ?? new List<CustomerDto>();
            ViewBag.Parties = new SelectList(activeCustomers, "Id", "Name");

            // Detailed is the reference default; "summary" stays available
            // through the on-screen toggle and the URL.
            var viewMode = string.Equals(mode, "summary", StringComparison.OrdinalIgnoreCase)
                ? "summary"
                : "detailed";
            ViewBag.Mode = viewMode;

            if (id == null || id <= 0)
                return View();

            var customer = activeCustomers.FirstOrDefault(c => c.Id == id)
                ?? (await _customerService.GetAllCustomersAsync() ?? new List<CustomerDto>()).FirstOrDefault(c => c.Id == id);
            if (customer == null) return NotFound();

            var model = await StatementPageDataLoader.LoadAsync(
                StatementAccountType.Customer, customer.Id, customer.Name, "Customer", from, to, viewMode,
                customer.PhoneNumber, customer.Address,
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
        public async Task<IActionResult> Create(CreateCustomerDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _customerService.CreateCustomerAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم إضافة العميل بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء إضافة العميل. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // EDIT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var customer = await _customerService.GetCustomerByIdAsync(id);
            if (customer == null) return NotFound();

            var dto = new UpdateCustomerDto
            {
                Id = customer.Id,
                Name = customer.Name,
                PhoneNumber = customer.PhoneNumber,
                Address = customer.Address,
                IsActive = customer.IsActive
            };

            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateCustomerDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            var result = await _customerService.UpdateCustomerAsync(dto);
            if (result)
            {
                TempData["SuccessMessage"] = "تم تحديث بيانات العميل بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "حدث خطأ أثناء تحديث بيانات العميل. تأكد من البيانات وحاول مرة أخرى.");
            return View(dto);
        }

        // ==========================================
        // TOGGLE STATUS (Activate / Deactivate)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var result = await _customerService.ToggleStatusAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم تغيير حالة العميل بنجاح.";
            else
                TempData["ErrorMessage"] = "العميل غير موجود.";

            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // DELETE (Soft delete via service)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _customerService.DeleteCustomerAsync(id);
            if (result)
                TempData["SuccessMessage"] = "تم إلغاء تنشيط العميل بنجاح.";
            else
                TempData["ErrorMessage"] = "العميل غير موجود.";

            return RedirectToAction(nameof(Index));
        }
    }
}
