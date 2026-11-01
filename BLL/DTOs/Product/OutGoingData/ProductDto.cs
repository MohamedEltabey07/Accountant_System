using DAL.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.DTOs.Product.OutGoingData
{
    public class ProductDto
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public decimal PurchasePrice { get; set; }

        public decimal WholesalePrice { get; set; }

        public decimal RetailPrice { get; set; }

        public bool IsActive { get; set; }

        public ProductCategory Category { get; set; }

        public decimal? DefaultCartonWeightKg { get; set; }

        public decimal? MaritimePricePerKg { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}
