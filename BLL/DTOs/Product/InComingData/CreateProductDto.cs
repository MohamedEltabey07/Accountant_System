using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BLL.DTOs.Product.InComingData
{
    public class CreateProductDto
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; }

        [Required]
        [Range(0,int.MaxValue,ErrorMessage ="The number must be greater than 0")]
        public decimal PurchasePrice { get; set; }

        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "The number must be greater than 0")]
        public decimal WholesalePrice { get; set; }

        [Required]
        [Range(0, int.MaxValue, ErrorMessage = "The number must be greater than 0")]
        public decimal RetailPrice { get; set; }
    }
}
