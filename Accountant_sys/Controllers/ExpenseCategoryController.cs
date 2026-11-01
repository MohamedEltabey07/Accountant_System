using BLL.DTOs.ExpenseCategory.InComingData;
using BLL.DTOs.ExpenseCategory.OutGoingData;
using BLL.IService;
using Microsoft.AspNetCore.Mvc;

namespace Accountant_sys.Controllers
{
    public class ExpenseCategoryController : Controller
    {
        private readonly IExpenseCategoryService _expenseCategoryService;

        public ExpenseCategoryController(IExpenseCategoryService expenseCategoryService)
        {
            _expenseCategoryService = expenseCategoryService;
        }

        // ==========================================
        // LIST
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var categories = await _expenseCategoryService.GetAllCategoriesAsync();
            return View(categories ?? new List<ExpenseCategoryDto>());
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
        public async Task<IActionResult> Create(CreateExpenseCategoryDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                var result = await _expenseCategoryService.CreateCategoryAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم إضافة بند المصروف بنجاح.";
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError(string.Empty, "حدث خطأ أثناء إضافة بند المصروف. تأكد من البيانات وحاول مرة أخرى.");
            }
            catch (ArgumentNullException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "بيانات بند المصروف غير مكتملة." : ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء إضافة بند المصروف." : ex.Message);
            }

            return View(dto);
        }

        // ==========================================
        // EDIT
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var categories = await _expenseCategoryService.GetAllCategoriesAsync();
            var category = (categories ?? new List<ExpenseCategoryDto>()).FirstOrDefault(c => c.Id == id);
            if (category == null)
                return NotFound();

            var dto = new UpdateExpenseCategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                IsActive = category.IsActive
            };

            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UpdateExpenseCategoryDto dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                var result = await _expenseCategoryService.UpdateCategoryAsync(dto);
                if (result)
                {
                    TempData["SuccessMessage"] = "تم تحديث بند المصروف بنجاح.";
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError(string.Empty, "حدث خطأ أثناء تحديث بند المصروف. تأكد من البيانات وحاول مرة أخرى.");
            }
            catch (NullReferenceException)
            {
                ModelState.AddModelError(string.Empty, "بند المصروف غير موجود بالنظام.");
            }
            catch (ArgumentNullException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "بيانات بند المصروف غير مكتملة." : ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء تحديث بند المصروف." : ex.Message);
            }

            return View(dto);
        }

        // ==========================================
        // TOGGLE STATUS (Activate / Deactivate)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            try
            {
                var result = await _expenseCategoryService.ToggleCategoryStatusAsync(id);
                if (result)
                    TempData["SuccessMessage"] = "تم تغيير حالة بند المصروف بنجاح.";
                else
                    TempData["ErrorMessage"] = "بند المصروف غير موجود.";
            }
            catch (NullReferenceException)
            {
                TempData["ErrorMessage"] = "بند المصروف غير موجود.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
