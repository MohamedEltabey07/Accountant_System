using BLL.DTOs.DailyClosing.InComingData;
using BLL.DTOs.DailyClosing.OutGoingData;
using System;

namespace Accountant_sys.Models
{
    public class CloseDayViewModel
    {
        public CreateDailyClosingDto Form { get; set; } = new();
        public DailyClosingDto Preview { get; set; } = new();
        public bool IsAlreadyClosed { get; set; }
        public DailyClosingDto? ExistingClosing { get; set; }
    }
}
