using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs.ExpenseCategory.InComingData
{
    public class CreateExpenseCategoryDto
    {
        [Required(ErrorMessage = "اسم بند المصروف مطلوب")]
        [StringLength(100, ErrorMessage = "اسم بند المصروف لا يتجاوز 100 حرف")]
        public string Name { get; set; } = string.Empty;
    }
}
