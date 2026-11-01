using Accountant_sys.Models;
using BLL.DTOs.DailyClosing.InComingData;
using BLL.IService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Accountant_sys.Controllers
{
    [Authorize]
    public class DailyClosingController : Controller
    {
        private readonly IDailyClosingService _dailyClosingService;

        public DailyClosingController(IDailyClosingService dailyClosingService)
        {
            _dailyClosingService = dailyClosingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to)
        {
            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");

            var history = await _dailyClosingService.GetClosingHistoryAsync(from, to);
            return View(history);
        }

        [HttpGet]
        public async Task<IActionResult> CloseDay(DateTime? date)
        {
            var targetDate = (date ?? DateTime.Today).Date;
            var isClosed = await _dailyClosingService.IsDateClosedAsync(targetDate);
            var preview = await _dailyClosingService.CalculatePreviewClosingAsync(targetDate);

            var viewModel = new CloseDayViewModel
            {
                IsAlreadyClosed = isClosed,
                Preview = preview,
                Form = new CreateDailyClosingDto
                {
                    BusinessDate = targetDate,
                    OpeningCashAmount = preview.OpeningCashAmount,
                    CountedCashAmount = 0m
                }
            };

            if (isClosed)
            {
                viewModel.ExistingClosing = await _dailyClosingService.GetClosingByDateAsync(targetDate);
            }

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseDay(CloseDayViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var targetDate = model.Form.BusinessDate.Date;
                model.Preview = await _dailyClosingService.CalculatePreviewClosingAsync(targetDate);
                model.IsAlreadyClosed = await _dailyClosingService.IsDateClosedAsync(targetDate);
                return View(model);
            }

            try
            {
                var result = await _dailyClosingService.PerformDailyClosingAsync(model.Form);
                if (result)
                {
                    TempData["Success"] = $"تم إقفال وردية يوم {model.Form.BusinessDate:yyyy-MM-dd} بنجاح.";
                    return RedirectToAction(nameof(Details), new { date = model.Form.BusinessDate.ToString("yyyy-MM-dd") });
                }
                else
                {
                    TempData["Error"] = "تعذر إتمام الإقفال اليومي. يرجى مراجعة البيانات.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            var fallbackDate = model.Form.BusinessDate.Date;
            model.Preview = await _dailyClosingService.CalculatePreviewClosingAsync(fallbackDate);
            model.IsAlreadyClosed = await _dailyClosingService.IsDateClosedAsync(fallbackDate);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Details(DateTime? date)
        {
            var targetDate = (date ?? DateTime.Today).Date;
            var closing = await _dailyClosingService.GetClosingByDateAsync(targetDate);
            if (closing == null)
            {
                TempData["Warning"] = $"لا يوجد سجل إقفال مسجل ليوم {targetDate:yyyy-MM-dd}.";
                return RedirectToAction(nameof(CloseDay), new { date = targetDate.ToString("yyyy-MM-dd") });
            }

            return View(closing);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reopen(ReopenDailyClosingDto dto)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "بيانات إعادة الفتح غير مكتملة، يجب تحديد السبب.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var result = await _dailyClosingService.ReopenDailyClosingAsync(dto);
                if (result)
                {
                    TempData["Success"] = "تم إعادة فتح اليومية بنجاح، ويمكنك الآن تعديل الحركات وإعادة الإقفال.";
                }
                else
                {
                    TempData["Error"] = "تعذر إعادة فتح اليومية.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
