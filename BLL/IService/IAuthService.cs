using BLL.DTOs.Auth;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.IService
{
    public interface IAuthService
    {
        Task<bool> HasAnyAdminAsync();
        Task<bool> RegisterFirstAdminAsync(RegisterAdminDto dto);
        Task<bool> LoginAsync(LoginDto dto);
        Task LogoutAsync();
        Task<bool> ChangePasswordAsync(string userId, ChangePasswordDto dto);
    }
}
