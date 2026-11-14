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

            ViewBag.Mode = string.Equals(mode, "detailed", StringComparison.OrdinalIgnoreCase)
                ? "detailed"
                : "summary";

            if (id == null || id <= 0)
                return View();

            var company = activeCompanies.FirstOrDefault(c => c.Id == id)
                ?? (await _maritimeCompanyService.GetAllCompaniesAsync() ?? new List<MaritimeCompanyDto>()).FirstOrDefault(c => c.Id == id);
            if (company == null) return NotFound();

            ViewBag.SelectedCompanyId = company.Id;

            var fromDate = from ?? StatementBuilder.HistoryStart;
            var toDate = to ?? StatementBuilder.HistoryEnd;

            var ledger = await BuildMaritimeLedgerAsync(id.Value, fromDate, toDate);
            var model = StatementBuilder.Build(ledger, isVendor: false, company.Id, company.Name, "MaritimeCompany", from, to);
            model.Mode = (string)ViewBag.Mode;

            // The detailed supply-orders mode needs the full order bodies
            // (external number + every item + per-order total) — same posted
            // invoices the summary ledger is built from.
            if (ViewBag.Mode == "detailed")
            {
                ViewBag.SupplyOrders = await _invoiceService.GetMaritimeSupplyOrdersAsync(company.Id, from, to)
                    ?? new List<BLL.DTOs.MaritimeCompany.OutGoingData.MaritimeSupplyOrderDto>();
            }

            return View(model);
        }

        private async Task<List<StatementLine>> BuildMaritimeLedgerAsync(int companyId, DateTime from, DateTime to)
        {
            var lines = new List<StatementLine>();
            var historyStart = StatementBuilder.HistoryStart;

            var openings = await _openingBalanceService.GetAllOpeningBalancesAsync() ?? new List<OpeningBalanceDto>();
            var opening = openings.FirstOrDefault(o => o.CustomerId == companyId && o.VendorId == null);
            if (opening != null)
            {
                lines.Add(new StatementLine
                {
                    Date = opening.EffectiveDate,
                    Timestamp = opening.CreatedAt,
                    SortGroup = 0,
                    Kind = StatementLineKind.OpeningBalance,
                    DocumentId = opening.Id,
                    DocumentNo = "OP" + opening.Id.ToString("D4"),
                    DocumentType = "رصيد افتتاحي",
                    Description = StatementBuilder.FormatOpeningDescription(opening),
                    Notes = opening.Notes,
                    Debit = opening.Amount
                });
            }

            var supplies = await _invoiceService.GetInvoicesAsync(InvoiceType.MaritimeSupply, InvoiceStatus.Posted)
                ?? new List<InvoiceDto>();
            var companySupplies = supplies.Where(i => i.CustomerId == companyId).ToList();
            foreach (var supply in companySupplies)
            {
                var externalNo = string.IsNullOrWhiteSpace(supply.ExternalOrderNumber)
                    ? supply.InvoiceNumber
                    : supply.ExternalOrderNumber;
                lines.Add(new StatementLine
                {
                    Date = supply.TransactionDate,
                    Timestamp = supply.CreatedAt,
                    Kind = StatementLineKind.MaritimeSupplyOrder,
                    DocumentId = supply.Id,
                    DocumentNo = externalNo,
                    SecondaryDocNo = string.IsNullOrWhiteSpace(supply.ExternalOrderNumber) ? null : supply.InvoiceNumber,
                    DocumentType = "أمر توريد بحري (مبيعات)",
                    Description = StatementBuilder.FormatInvoiceDescription(supply, isVendor: false, isMaritime: true),
                    Notes = supply.Notes,
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
                    Timestamp = note.CreatedAt,
                    Kind = StatementLineKind.CreditNoteReturn,
                    DocumentId = note.Id,
                    DocumentNo = note.CreditNoteNumber,
                    DocumentType = "إشعار مرتجع (مسترد)",
                    Description = StatementBuilder.FormatCreditNoteDescription(note),
                    Notes = note.Reason,
                    CreditNote = note,
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
                    Timestamp = payment.CreatedAt,
                    Kind = StatementLineKind.CustomerReceipt,
                    DocumentId = payment.Id,
                    DocumentNo = "REC" + payment.Id.ToString("D6"),
                    DocumentType = "سند دفعة مستلمة (بحري)",
                    Description = StatementBuilder.FormatPaymentDescription(payment, isVendor: false),
                    Notes = payment.Notes,
                    CashTransaction = payment,
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
                    Timestamp = refund.CreatedAt,
                    Kind = StatementLineKind.CustomerRefund,
                    DocumentId = refund.Id,
                    DocumentNo = "RFD" + refund.Id.ToString("D6"),
                    DocumentType = "سند مرتجع نقدي (مردود للشركة)",
                    Description = string.IsNullOrWhiteSpace(refund.Notes) ? "رد نقدي للشركة البحرية" : refund.Notes.Trim(),
                    Notes = refund.Notes,
                    CashTransaction = refund,
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
