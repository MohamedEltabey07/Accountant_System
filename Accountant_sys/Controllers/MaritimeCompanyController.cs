using Accountant_sys.Models;
using BLL.DTOs.Cash.OutGoingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Customer.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.MaritimeCompany.InComingData;
using BLL.DTOs.MaritimeCompany.OutGoingData;
using BLL.DTOs.OpeningBalance.OutGoingData;
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
        public async Task<IActionResult> Index()
        {
            var companies = await _maritimeCompanyService.GetAllCompaniesAsync() ?? new List<MaritimeCompanyDto>();
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
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Statement(int? id, DateTime? from, DateTime? to)
        {
            var activeCompanies = await _maritimeCompanyService.GetActiveCompaniesAsync() ?? new List<MaritimeCompanyDto>();
            ViewBag.Parties = new SelectList(activeCompanies, "Id", "Name");

            if (id == null || id <= 0)
                return View();

            var company = activeCompanies.FirstOrDefault(c => c.Id == id)
                ?? (await _maritimeCompanyService.GetAllCompaniesAsync() ?? new List<MaritimeCompanyDto>()).FirstOrDefault(c => c.Id == id);
            if (company == null) return NotFound();

            var fromDate = from ?? DateTime.Today.AddYears(-10);
            var toDate = to ?? DateTime.Today;

            var ledger = await BuildMaritimeLedgerAsync(id.Value, fromDate, toDate);
            var model = StatementBuilder.Build(ledger, isVendor: false, company.Id, company.Name, "MaritimeCompany", from, to);

            return View(model);
        }

        private async Task<List<StatementLine>> BuildMaritimeLedgerAsync(int companyId, DateTime from, DateTime to)
        {
            var lines = new List<StatementLine>();
            var historyStart = new DateTime(2000, 1, 1);

            var openings = await _openingBalanceService.GetAllOpeningBalancesAsync() ?? new List<OpeningBalanceDto>();
            var opening = openings.FirstOrDefault(o => o.CustomerId == companyId && o.VendorId == null);
            if (opening != null)
            {
                lines.Add(new StatementLine
                {
                    Date = opening.EffectiveDate,
                    DocumentNo = "OP" + opening.Id.ToString("D4"),
                    DocumentType = "رصيد افتتاحي",
                    Debit = opening.Amount
                });
            }

            var supplies = await _invoiceService.GetInvoicesAsync(InvoiceType.MaritimeSupply, InvoiceStatus.Posted)
                ?? new List<InvoiceDto>();
            var companySupplies = supplies.Where(i => i.CustomerId == companyId).ToList();
            foreach (var supply in companySupplies)
            {
                lines.Add(new StatementLine
                {
                    Date = supply.TransactionDate,
                    DocumentId = supply.Id,
                    DocumentNo = supply.InvoiceNumber,
                    DocumentType = "أمر توريد بحري (مبيعات)",
                    Debit = supply.TotalAmount
                });
            }

            var returns = await _creditNoteService.GetCreditNotesAsync(
                CreditNoteType.CustomerReturn, CreditNoteStatus.Posted, historyStart, to)
                ?? new List<CreditNoteDto>();
            var supplyIds = companySupplies.Select(i => i.Id).ToHashSet();
            foreach (var note in returns.Where(n => supplyIds.Contains(n.OriginalInvoiceId)))
            {
                lines.Add(new StatementLine
                {
                    Date = note.TransactionDate,
                    DocumentId = note.Id,
                    DocumentNo = note.CreditNoteNumber,
                    DocumentType = "إشعار مرتجع (مسترد)",
                    Credit = note.TotalAmount
                });
            }

            var payments = await _cashTransactionService.GetTransactionsAsync(
                historyStart, to, CashCategory.CustomerPayment, CashDirection.In)
                ?? new List<CashTransactionDto>();
            foreach (var payment in payments.Where(p => p.CustomerId == companyId))
            {
                lines.Add(new StatementLine
                {
                    Date = payment.TransactionDate,
                    DocumentId = payment.Id,
                    DocumentNo = "REC" + payment.Id.ToString("D6"),
                    DocumentType = "سند دفعة مستلمة (بحري)",
                    Credit = payment.Amount
                });
            }

            var refunds = await _cashTransactionService.GetTransactionsAsync(
                historyStart, to, CashCategory.CustomerRefund, CashDirection.Out)
                ?? new List<CashTransactionDto>();
            foreach (var refund in refunds.Where(p => p.CustomerId == companyId))
            {
                lines.Add(new StatementLine
                {
                    Date = refund.TransactionDate,
                    DocumentId = refund.Id,
                    DocumentNo = "RFD" + refund.Id.ToString("D6"),
                    DocumentType = "سند مرتجع نقدي (مردود للشركة)",
                    Debit = refund.Amount
                });
            }

            return lines;
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
