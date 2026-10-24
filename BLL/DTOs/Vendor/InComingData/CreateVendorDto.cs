using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.Vendor.InComingData
{
    public class CreateVendorDto
    {
        [Required(ErrorMessage = "اسم المورد مطلوب")]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(11)]
        [RegularExpression("^01[0125]\\d{8}$", ErrorMessage = "رقم الهاتف غير صحيح")]
        public string? PhoneNumber { get; set; }

        public string? Address { get; set; }
    }
}
