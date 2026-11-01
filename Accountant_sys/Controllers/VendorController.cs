using Accountant_sys.Models;
using BLL.DTOs.Cash.OutGoingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.DTOs.OpeningBalance.OutGoingData;
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
        // ACCOUNT STATEMENT (كشف حساب مورد)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Statement(int? id, DateTime? from, DateTime? to)
        {
            var activeVendors = await _vendorService.GetActiveVendorAsync() ?? new List<VendorDto>();
            ViewBag.Parties = new SelectList(activeVendors, "Id", "Name");

            if (id == null || id <= 0)
                return View();

            var vendor = activeVendors.FirstOrDefault(v => v.Id == id)
                ?? (await _vendorService.GetAllVendorAsync() ?? new List<VendorDto>()).FirstOrDefault(v => v.Id == id);
            if (vendor == null) return NotFound();
            var fromDate = from ?? DateTime.Today.AddYears(-10);
            var toDate = to ?? DateTime.Today;
            var ledger = await BuildVendorLedgerAsync(id.Value, fromDate, toDate);
            var model = StatementBuilder.Build(ledger, isVendor: true, vendor.Id, vendor.Name, "Vendor", from, to);
            return View(model);
        }

        private async Task<List<StatementLine>> BuildVendorLedgerAsync(int vendorId, DateTime from, DateTime to)
        {
            var lines = new List<StatementLine>();
            var historyStart = StatementBuilder.HistoryStart;

            var openings = await _openingBalanceService.GetAllOpeningBalancesAsync() ?? new List<OpeningBalanceDto>();
            var opening = openings.FirstOrDefault(o => o.VendorId == vendorId);
            if (opening != null)
            {
                lines.Add(new StatementLine
                {
                    Date = opening.EffectiveDate,
                    DocumentNo = "OP" + opening.Id.ToString("D4"),
                    DocumentType = "رصيد افتتاحي",
                    Credit = opening.Amount
                });
            }

            var invoices = await _invoiceService.GetInvoicesAsync(InvoiceType.VendorPurchase, InvoiceStatus.Posted) ?? new List<InvoiceDto>();
            var vendorInvoices = invoices.Where(i => i.VendorId == vendorId).ToList();
            foreach (var invoice in vendorInvoices)
            {
                lines.Add(new StatementLine
                {
                    Date = invoice.TransactionDate,
                    DocumentId = invoice.Id,
                    DocumentNo = invoice.InvoiceNumber,
                    DocumentType = "فاتورة مشتريات",
                    Credit = invoice.TotalAmount
                });
            }

            var returns = await _creditNoteService.GetCreditNotesAsync(
                CreditNoteType.VendorReturn, CreditNoteStatus.Posted,
                historyStart, to) ?? new List<CreditNoteDto>();
            var invoiceIds = vendorInvoices.Select(i => i.Id).ToHashSet();
            foreach (var note in returns.Where(n => invoiceIds.Contains(n.OriginalInvoiceId)))
            {
                lines.Add(new StatementLine
                {
                    Date = note.TransactionDate,
                    DocumentId = note.Id,
                    DocumentNo = note.CreditNoteNumber,
                    DocumentType = "إشعار مرتجع",
                    Debit = note.TotalAmount
                });
            }

            var payments = await _cashTransactionService.GetTransactionsAsync(
                historyStart, to, CashCategory.VendorPayment, CashDirection.Out) ?? new List<CashTransactionDto>();
            foreach (var payment in payments.Where(p => p.VendorId == vendorId))
            {
                lines.Add(new StatementLine
                {
                    Date = payment.TransactionDate,
                    DocumentId = payment.Id,
                    DocumentNo = "VCH" + payment.Id.ToString("D6"),
                    DocumentType = "سند صرف (دفعة)",
                    Debit = payment.Amount
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
