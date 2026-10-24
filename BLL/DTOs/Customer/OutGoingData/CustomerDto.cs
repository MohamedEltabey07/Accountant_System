using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace BLL.DTOs.Customer.OutGoingData
{
    public class CustomerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal CurrentBalance { get; set; }
    }
}
