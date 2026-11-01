using Accountant_sys.Models;
using BLL.DTOs.CreditNote.InComingData;
using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Invoice.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class CreditNoteController : Controller
    {
        private readonly ICreditNoteService _creditNoteService;
        private readonly IInvoiceService _invoiceService;

        public CreditNoteController(ICreditNoteService creditNoteService, IInvoiceService invoiceService)
        {
            _creditNoteService = creditNoteService;
            _invoiceService = invoiceService;
        }

        // ==========================================
        // LIST (with filters)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(int? type, int? status)
        {
            CreditNoteType? parsedType = null;
            CreditNoteStatus? parsedStatus = null;

            if (Enum.IsDefined(typeof(CreditNoteType), type ?? 0))
                parsedType = (CreditNoteType)type;

            if (Enum.IsDefined(typeof(CreditNoteStatus), status ?? 0))
                parsedStatus = (CreditNoteStatus)status;

            var notes = await _creditNoteService.GetCreditNotesAsync(
                parsedType, parsedStatus, DateTime.Now.AddYears(-1), DateTime.Now.AddYears(1));

            ViewBag.SelectedType = type;
            ViewBag.SelectedStatus = status;
            ViewBag.TypeOptions = GetTypeSelectList();
            ViewBag.StatusOptions = GetStatusSelectList();

            return View(notes ?? new List<CreditNoteDto>());
        }

        // ==========================================
        // CREATE DRAFT RETURN
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Create(int? invoiceId)
        {
            ViewBag.PostedInvoices = await PreparePostedInvoiceSelectListAsync();
            ViewBag.SelectedInvoiceId = invoiceId;

            if (invoiceId == null || invoiceId <= 0)
                return View();

            var invoice = await _invoiceService.GetInvoiceByIdAsync(invoiceId.Value);
            if (invoice == null || invoice.Status != InvoiceStatus.Posted)
            {
                TempData["ErrorMessage"] = "الفاتورة المحددة غير موجودة أو غير مرحلة.";
                return View();
            }

            var postedNotes = await _creditNoteService.GetCreditNotesAsync(
                null, CreditNoteStatus.Posted, DateTime.Now.AddYears(-1), DateTime.Now.AddYears(1));

            var lines = BuildCreditNoteLines(invoice, postedNotes ?? new List<CreditNoteDto>());
            ViewBag.Lines = lines;

            return View(new CreateCreditNoteDraftDto { OriginalInvoiceId = invoice.Id, TransactionDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateCreditNoteDraftDto dto)
        {
            ViewBag.PostedInvoices = await PreparePostedInvoiceSelectListAsync();

            if (!ModelState.IsValid)
            {
                ViewBag.SelectedInvoiceId = dto.OriginalInvoiceId;
                await RestoreLinesAsync(dto.OriginalInvoiceId);
                return View(dto);
            }

            try
            {
                var creditNoteId = await _creditNoteService.CreateDraftCreditNoteAsync(dto);
                TempData["SuccessMessage"] = "تم إنشاء المرتجع كمسودة بنجاح. يمكنك مراجعته ثم ترحيله.";
                return RedirectToAction(nameof(Details), new { id = creditNoteId });
            }
            catch (ArgumentNullException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "بيانات المرتجع غير مكتملة." : ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء إنشاء المرتجع." : ex.Message);
            }

            ViewBag.SelectedInvoiceId = dto.OriginalInvoiceId;
            await RestoreLinesAsync(dto.OriginalInvoiceId);
            return View(dto);
        }

        // ==========================================
        // DETAILS (draft preview / posted printable)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var note = await _creditNoteService.GetCreditNoteByIdAsync(id);
            if (note == null)
                return NotFound();

            return View(note);
        }

        // ==========================================
        // POST RETURN (irreversible)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(PostCreditNoteDto dto)
        {
            try
            {
                var result = await _creditNoteService.PostCreditNoteAsync(dto);
                if (result)
                    TempData["SuccessMessage"] = "تم ترحيل المرتجع بنجاح وتعديل رصيد الفاتورة الأصلية.";
                else
                    TempData["ErrorMessage"] = "تعذر ترحيل المرتجع.";
            }
            catch (ArgumentNullException ex)
            {
                TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(ex.Message) ? "بيانات الترحيل غير مكتملة." : ex.Message;
            }
            catch (NullReferenceException ex)
            {
                TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء الترحيل." : ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء الترحيل." : ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id = dto.CreditNoteId });
        }

        // ==========================================
        // PRIVATE HELPERS
        // ==========================================
        private async Task<SelectList> PreparePostedInvoiceSelectListAsync()
        {
            var postedInvoices = await _invoiceService.GetInvoicesAsync(null, InvoiceStatus.Posted) ?? new List<InvoiceDto>();
            var displayItems = postedInvoices.Select(i => new
            {
                i.Id,
                DisplayName = $"{i.InvoiceNumber} — {(i.CustomerId != null ? i.CustomerName : i.VendorName)} — {i.TotalAmount:N2}"
            });
            return new SelectList(displayItems, "Id", "DisplayName");
        }

        private async Task RestoreLinesAsync(int originalInvoiceId)
        {
            if (originalInvoiceId <= 0)
            {
                ViewBag.Lines = new List<CreditNoteLineViewModel>();
                return;
            }

            var invoice = await _invoiceService.GetInvoiceByIdAsync(originalInvoiceId);
            if (invoice == null)
            {
                ViewBag.Lines = new List<CreditNoteLineViewModel>();
                return;
            }

            var postedNotes = await _creditNoteService.GetCreditNotesAsync(
                null, CreditNoteStatus.Posted, DateTime.Now.AddYears(-1), DateTime.Now.AddYears(1));

            ViewBag.Lines = BuildCreditNoteLines(invoice, postedNotes ?? new List<CreditNoteDto>());
        }

        private List<CreditNoteLineViewModel> BuildCreditNoteLines(InvoiceDto invoice, List<CreditNoteDto> postedNotes)
        {
            var returnedQtyByProduct = postedNotes
                .Where(n => n.OriginalInvoiceId == invoice.Id)
                .SelectMany(n => n.Items)
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

            return invoice.Items.Select(item =>
            {
                returnedQtyByProduct.TryGetValue(item.ProductId, out var alreadyReturned);
                return new CreditNoteLineViewModel
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    OriginalQuantity = item.Quantity,
                    AlreadyReturned = alreadyReturned,
                    MaxReturnable = item.Quantity - alreadyReturned,
                    UnitPrice = item.UnitPrice
                };
            }).ToList();
        }

        #region Select Lists
        private dynamic GetTypeSelectList()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "جميع الأنواع" },
                new SelectListItem { Value = ((int)CreditNoteType.CustomerReturn).ToString(), Text = "مرتجع عميل" },
                new SelectListItem { Value = ((int)CreditNoteType.VendorReturn).ToString(), Text = "مرتجع مورد" }
            };
        }

        private dynamic GetStatusSelectList()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "جميع الحالات" },
                new SelectListItem { Value = ((int)CreditNoteStatus.Draft).ToString(), Text = "مسودة" },
                new SelectListItem { Value = ((int)CreditNoteStatus.Posted).ToString(), Text = "مرحّل" },
                new SelectListItem { Value = ((int)CreditNoteStatus.Cancelled).ToString(), Text = "ملغى" }
            };
        }
        #endregion
    }
}
