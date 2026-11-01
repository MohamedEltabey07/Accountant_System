using BLL.DTOs.Backup.InComingData;
using BLL.IService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Accountant_sys.Controllers
{
    [Authorize]
    public class BackupController : Controller
    {
        private readonly IBackupService _backupService;

        public BackupController(IBackupService backupService)
        {
            _backupService = backupService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var backups = await _backupService.GetBackupListAsync();
            ViewBag.BackupFolder = _backupService.GetBackupFolder();
            return View(backups);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBackup()
        {
            var result = await _backupService.CreateBackupAsync();

            if (result.Success)
                TempData["SuccessMessage"] = result.Message;
            else
                TempData["ErrorMessage"] = $"{result.Message} {result.Error}";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreBackup(RestoreBackupDto dto)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "يجب تحديد ملف النسخة الاحتياطية المطلوب استرجاعه.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _backupService.RestoreBackupAsync(dto.FileName);

            if (result.Success)
                TempData["SuccessMessage"] = result.Message + " تم إعادة تشغيل عمليات النظام بشكل طبيعي.";
            else
                TempData["ErrorMessage"] = $"{result.Message} {result.Error}";

            return RedirectToAction(nameof(Index));
        }
    }
}
