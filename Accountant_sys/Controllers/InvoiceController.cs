using BLL.DTOs.CreditNote.OutGoingData;
using BLL.DTOs.Customer.OutGoingData;
using BLL.DTOs.Invoice.InComingData;
using BLL.DTOs.Product.OutGoingData;
using BLL.DTOs.Vendor.OutGoingData;
using BLL.IService;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Accountant_sys.Controllers
{
    public class InvoiceController : Controller
    {
        private readonly IInvoiceService _invoiceService;
        private readonly IProductService _productService;
        private readonly ICustomerService _customerService;
        private readonly IVendorService _vendorService;
        private readonly ICreditNoteService _creditNoteService;
        private readonly IMaritimeCompanyService _maritimeCompanyService;

        public InvoiceController(IInvoiceService invoiceService, IProductService productService,
            ICustomerService customerService, IVendorService vendorService, ICreditNoteService creditNoteService,
            IMaritimeCompanyService maritimeCompanyService)
        {
            _invoiceService = invoiceService;
            _productService = productService;
            _customerService = customerService;
            _vendorService = vendorService;
            _creditNoteService = creditNoteService;
            _maritimeCompanyService = maritimeCompanyService;
        }

        // ==========================================
        // LIST (filterable by type & status)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index(string? type, string? status)
        {
            InvoiceType? parsedType = null;
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (Enum.TryParse<InvoiceType>(type, true, out var t))
                    parsedType = t;
                else if (int.TryParse(type, out var tInt) && Enum.IsDefined(typeof(InvoiceType), tInt))
                    parsedType = (InvoiceType)tInt;
            }

            InvoiceStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (Enum.TryParse<InvoiceStatus>(status, true, out var s))
                    parsedStatus = s;
                else if (int.TryParse(status, out var sInt) && Enum.IsDefined(typeof(InvoiceStatus), sInt))
                    parsedStatus = (InvoiceStatus)sInt;
            }

            ViewBag.SelectedType = parsedType;
            ViewBag.SelectedStatus = parsedStatus;

            var invoices = await _invoiceService.GetInvoicesAsync(parsedType, parsedStatus);
            return View(invoices);
        }

        // ==========================================
        // DETAILS (draft management + posted view)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
            if (invoice == null) return NotFound();

            var allNotes = await _creditNoteService.GetCreditNotesAsync(null, null, DateTime.Now.AddYears(-5), DateTime.Now.AddYears(1));
            var invoiceCreditNotes = (allNotes ?? new List<CreditNoteDto>())
                .Where(cn => cn.OriginalInvoiceId == id)
                .ToList();

            ViewBag.CreditNotes = invoiceCreditNotes;

            var returnedQuantities = invoiceCreditNotes
                .Where(cn => cn.Status == CreditNoteStatus.Posted)
                .SelectMany(cn => cn.Items)
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

            ViewBag.ReturnedQuantities = returnedQuantities;

            ViewBag.TotalReturnedAmount = invoiceCreditNotes
                .Where(cn => cn.Status == CreditNoteStatus.Posted)
                .Sum(cn => cn.TotalAmount);

            await PrepareProductSelectList(invoice.Type);
            return View(invoice);
        }

        // ==========================================
        // CREATE DRAFT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Create(InvoiceType? type, int? customerId, int? vendorId)
        {
            await PreparePartySelectLists();
            var dto = new CreateInvoiceDraftDto
            {
                Type = type ?? InvoiceType.WholesaleSales,
                TransactionDate = DateTime.Today,
                CustomerId = customerId,
                VendorId = vendorId
            };
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateInvoiceDraftDto dto)
        {
            await PreparePartySelectLists();

            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                var draftId = await _invoiceService.CreateDraftInvoiceAsync(dto);
                if (draftId > 0)
                    return RedirectToAction(nameof(Details), new { id = draftId });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(dto);
            }

            ModelState.AddModelError(string.Empty, "تعذّر إنشاء المسودة. تحقق من صحة البيانات وحاول مرة أخرى.");
            return View(dto);
        }
        // ==========================================
        // ADD ITEM TO DRAFT
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItem(AddInvoiceItemDto dto)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "بيانات البند غير صحيحة.";
                return RedirectToAction(nameof(Details), new { id = dto.InvoiceId });
            }

            try
            {
                await _invoiceService.AddItemToDraftAsync(dto);
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = $"تعذر إضافة البند: {ex.Message}";
            }

            return RedirectToAction(nameof(Details), new { id = dto.InvoiceId });
        }

        // ==========================================
        // UPDATE ITEM ON DRAFT
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateItem(UpdateInvoiceItemDto dto, int invoiceId)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "بيانات البند غير صحيحة.";
                return RedirectToAction(nameof(Details), new { id = invoiceId });
            }

            var result = await _invoiceService.UpdateDraftItemAsync(dto);
            if (!result)
            {
                TempData["ErrorMessage"] = "تعذر تعديل البند. تأكد من أن الفاتورة مسودة.";
            }

            return RedirectToAction(nameof(Details), new { id = invoiceId });
        }

        // ==========================================
        // REMOVE ITEM FROM DRAFT
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveItem(int itemId, int invoiceId)
        {
            var result = await _invoiceService.RemoveItemFromDraftAsync(itemId);
            TempData[result ? "SuccessMessage" : "ErrorMessage"] = result
                ? "تم حذف البند بنجاح."
                : "تعذر حذف البند. تأكد من أن الفاتورة مسودة.";

            return RedirectToAction(nameof(Details), new { id = invoiceId });
        }

        // ==========================================
        // POST INVOICE (freezes financial snapshot)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int id)
        {
            try
            {
                var result = await _invoiceService.PostInvoiceAsync(id);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم ترحيل الفاتورة بنجاح واعتمادها في الحسابات.";
                }
                else
                {
                    TempData["ErrorMessage"] = "تعذر ترحيل الفاتورة. يجب أن تكون مسودة وتحتوي على بند واحد على الأقل.";
                }
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // ==========================================
        // CANCEL POSTED INVOICE
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Cancel(int id)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
            if (invoice == null) return NotFound();

            if (invoice.Status != InvoiceStatus.Posted)
            {
                TempData["ErrorMessage"] = "لا يمكن إلغاء إلا الفواتير المرحلة فقط.";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(new CancelInvoiceDto { InvoiceId = invoice.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(CancelInvoiceDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                var result = await _invoiceService.CancelPostedInvoiceAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم إلغاء الفاتورة بنجاح. (أوامر التوريد البحرية: أُعيدت الكميات للمخزون بحركات عكسية سجّلت في السجل).";
                    return RedirectToAction(nameof(Details), new { id = dto.InvoiceId });
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(dto);
            }

            ModelState.AddModelError(string.Empty, "تعذر إلغاء الفاتورة. لا يمكن إلغاء فاتورة لها مدفوعات أو مرتجعات مسجلة.");
            return View(dto);
        }

        // ==========================================
        // REMAINING BALANCE / PAYMENT STATUS (AJAX-friendly)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> RemainingBalance(int id)
        {
            var remaining = await _invoiceService.GetInvoiceRemainingBalanceAsync(id);
            return Json(remaining);
        }

        // ==========================================
        // HELPERS
        // ==========================================
        private async Task PreparePartySelectLists()
        {
            var customers = await _customerService.GetActiveCustomersAsync() ?? new List<CustomerDto>();
            var vendors = await _vendorService.GetActiveVendorAsync() ?? new List<VendorDto>();
            var maritimeCompanies = await _maritimeCompanyService.GetActiveCompaniesAsync()
                ?? new List<BLL.DTOs.MaritimeCompany.OutGoingData.MaritimeCompanyDto>();

            ViewBag.Customers = new SelectList(customers, "Id", "Name");
            ViewBag.Vendors = new SelectList(vendors, "Id", "Name");
            ViewBag.MaritimeCompanies = new SelectList(maritimeCompanies, "Id", "Name");
        }

        private async Task PrepareProductSelectList(InvoiceType invoiceType)
        {
            // Maritime supply orders accept frozen fish items only.
            var products = invoiceType == InvoiceType.MaritimeSupply
                ? await _productService.GetProductsByCategoryAsync(ProductCategory.FrozenFish) ?? new List<ProductDto>()
                : await _productService.GetActiveProductsAsync() ?? new List<ProductDto>();
            ViewBag.Products = new SelectList(products, "Id", "Name");
        }

        private static SelectList GetInvoiceTypeSelectList(InvoiceType? selected = null)
        {
            return new SelectList(new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "كل أنواع الفواتير" },
                new SelectListItem { Value = ((int)InvoiceType.VendorPurchase).ToString(), Text = "مشتريات من مورد" },
                new SelectListItem { Value = ((int)InvoiceType.WholesaleSales).ToString(), Text = "مبيعات جملة" },
                new SelectListItem { Value = ((int)InvoiceType.StoreCashSales).ToString(), Text = "مبيعات تجزئة" },
                new SelectListItem { Value = ((int)InvoiceType.MaritimeSupply).ToString(), Text = "توريد لشركة بحرية" }
            }, "Value", "Text", selected.HasValue ? (int?)selected.Value : null);
        }

        private static SelectList GetInvoiceStatusSelectList(InvoiceStatus? selected = null)
        {
            return new SelectList(new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "كل الحالات" },
                new SelectListItem { Value = ((int)InvoiceStatus.Draft).ToString(), Text = "مسودة" },
                new SelectListItem { Value = ((int)InvoiceStatus.Posted).ToString(), Text = "مرحلة" },
                new SelectListItem { Value = ((int)InvoiceStatus.Cancelled).ToString(), Text = "ملغاة" }
            }, "Value", "Text", selected.HasValue ? (int?)selected.Value : null);
        }
    }
}
