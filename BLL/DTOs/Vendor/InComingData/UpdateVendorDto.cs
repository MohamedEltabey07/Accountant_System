using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BLL.DTOs.Vendor.InComingData
{
    public class UpdateVendorDto
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        [MaxLength(11)]
        [RegularExpression("^01[0125]\\d{8}$")]
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
    }
}
