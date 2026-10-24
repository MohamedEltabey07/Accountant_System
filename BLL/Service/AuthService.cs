using AutoMapper;
using BLL.DTOs.Auth;
using BLL.IService;
using DAL.Entities;
using DAL.IConfiguration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace BLL.Service
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IMapper _mapper;

        public AuthService(
            UserManager<AppUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IMapper mapper,
            SignInManager<AppUser> signInManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _mapper = mapper;
            _signInManager = signInManager;
        }

        public async Task<bool> HasAnyAdminAsync()
        {
            return await _userManager.Users.AnyAsync();
        }

        public async Task<bool> RegisterFirstAdminAsync(RegisterAdminDto dto)
        {
            // Only allow if no users exist yet
            if (await HasAnyAdminAsync()) return false;

            var user = _mapper.Map<AppUser>(dto);

            // CreateAsync with password so it gets hashed
            var createResult = await _userManager.CreateAsync(user, dto.Password);
            if (!createResult.Succeeded) return false;

            // Ensure "Admin" role exists
            if (!await _roleManager.RoleExistsAsync("Admin"))
            {
                await _roleManager.CreateAsync(new IdentityRole("Admin"));
            }

            var roleResult = await _userManager.AddToRoleAsync(user, "Admin");
            return roleResult.Succeeded;
        }

        public async Task<bool> LoginAsync(LoginDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null || !user.IsActive) return false;

            var result = await _signInManager.PasswordSignInAsync(
                user, dto.Password, dto.RememberMe, lockoutOnFailure: false);

            return result.Succeeded;
        }

        public async Task LogoutAsync()
        {
            await _signInManager.SignOutAsync();
        }

        public async Task<bool> ChangePasswordAsync(string userId, ChangePasswordDto dto)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            var result = await _userManager.ChangePasswordAsync(
                user, dto.OldPassword, dto.NewPassword);

            return result.Succeeded;
        }
    }
}
