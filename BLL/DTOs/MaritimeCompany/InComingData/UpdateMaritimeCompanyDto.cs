using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.MaritimeCompany.InComingData
{
    public class UpdateMaritimeCompanyDto
    {
        [Required]
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم الشركة البحرية مطلوب")]
        [MaxLength(200, ErrorMessage = "اسم الشركة طويل جداً")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50, ErrorMessage = "رقم الهاتف طويل جداً")]
        [Phone(ErrorMessage = "رقم الهاتف غير صحيح")]
        public string? PhoneNumber { get; set; }

        [MaxLength(300, ErrorMessage = "العنوان طويل جداً")]
        public string? Address { get; set; }

        public bool IsActive { get; set; }
    }
}
