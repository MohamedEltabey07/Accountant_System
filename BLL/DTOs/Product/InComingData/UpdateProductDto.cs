using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace BLL.DTOs.Product.InComingData
{
    public class UpdateProductDto
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "The number must be greater than 0")]
        public decimal PurchasePrice { get; set; }
        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "The number must be greater than 0")]
        public decimal WholesalePrice { get; set; }
        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "The number must be greater than 0")]
        public decimal RetailPrice { get; set; }
        [Required]
        public bool IsActive { get; set; }
    }
}
