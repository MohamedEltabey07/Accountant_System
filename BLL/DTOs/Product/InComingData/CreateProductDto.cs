using DAL.Enums;
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

        // Frozen fish items are tracked in the freezer stock ledger.
        public ProductCategory Category { get; set; } = ProductCategory.RegularProduct;

        // Required when Category = FrozenFish (drives carton count math).
        [Range(0.001, double.MaxValue, ErrorMessage = "وزن الكرتونة يجب أن يكون أكبر من صفر")]
        public decimal? DefaultCartonWeightKg { get; set; }

        // Optional default price per KG for maritime supply orders.
        [Range(0, double.MaxValue, ErrorMessage = "سعر الكجم يجب أن يكون أكبر من أو يساوي صفر")]
        public decimal? MaritimePricePerKg { get; set; }
    }
}
