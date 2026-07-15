using ECommerce.Api.Models;
using ECommerce.Api.Services;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ECommerceDbContext _context;
    private readonly ITokenService _tokenService;

    public AuthController(UserManager<ApplicationUser> userManager, ECommerceDbContext context, ITokenService tokenService)
    {
        _userManager = userManager;
        _context = context;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
            return BadRequest("User already exists.");

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return Ok(new { Message = "User registered successfully." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = await _userManager.FindByEmailAsync(request.Email);
        
        if (user == null)
            return Unauthorized("Invalid email or password.");

        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized("Account is locked due to too many failed attempts. Try again later.");

        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        
        if (!isPasswordValid)
            return Unauthorized("Invalid email or password.");

        // Successful login, reset lockout
        await _userManager.ResetAccessFailedCountAsync(user);

        return await GenerateTokenResponse(user);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] TokenRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
            if (principal == null)
                return Unauthorized("Invalid access token");

            var jwtId = principal.Claims.First(x => x.Type == JwtRegisteredClaimNames.Jti).Value;

            var storedRefreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(x => x.Token == request.RefreshToken);

            if (storedRefreshToken == null)
                return Unauthorized("Refresh token does not exist");

            if (DateTime.UtcNow > storedRefreshToken.ExpiryDate)
                return Unauthorized("Refresh token has expired, user must log in again");

            if (storedRefreshToken.IsRevoked || storedRefreshToken.IsUsed)
                return Unauthorized("Refresh token is invalid");

            if (storedRefreshToken.JwtId != jwtId)
                return Unauthorized("Refresh token does not match the access token");

            // Update current token to used
            storedRefreshToken.IsUsed = true;
            _context.RefreshTokens.Update(storedRefreshToken);
            await _context.SaveChangesAsync();

            var user = await _userManager.FindByIdAsync(storedRefreshToken.UserId);
            if (user == null) return Unauthorized("User not found");

            return await GenerateTokenResponse(user);
        }
        catch
        {
            return Unauthorized("Invalid token format");
        }
    }

    private async Task<IActionResult> GenerateTokenResponse(ApplicationUser user)
    {
        var jwtToken = await _tokenService.GenerateAccessToken(user);
        
        var jwtId = new JwtSecurityTokenHandler().ReadJwtToken(jwtToken)
            .Claims.First(x => x.Type == JwtRegisteredClaimNames.Jti).Value;

        var refreshToken = _tokenService.GenerateRefreshToken(user, jwtId);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        // Note: To easily shift to HttpOnly approach later, we would simply do:
        // Response.Cookies.Append("RefreshToken", refreshToken.Token, new CookieOptions { HttpOnly = true, Secure = true });
        
        return Ok(new AuthResponse
        {
            AccessToken = jwtToken,
            RefreshToken = refreshToken.Token
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _userManager.Users.ToListAsync();
        var userList = new List<object>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userList.Add(new { user.Id, user.Email, Roles = roles });
        }

        return Ok(userList);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("assign-role")]
    public async Task<IActionResult> AssignRole([FromBody] AssignRoleRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null) return NotFound("User not found.");

        var validRoles = new[] { "Admin", "Customer", "SupportAgent", "FulfillmentStaff" };
        if (!validRoles.Contains(request.Role)) return BadRequest("Invalid role.");

        if (!await _userManager.IsInRoleAsync(user, request.Role))
        {
            await _userManager.AddToRoleAsync(user, request.Role);
        }

        return Ok(new { Message = $"Role {request.Role} assigned to {request.Email}" });
    }

    [HttpGet("test-roles")]
    public async Task<IActionResult> TestRoles()
    {
        var adminEmail = "admin@enterprisestore.com";
        var adminUser = await _userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null) return NotFound("Admin user not found in DB.");
        
        var roles = await _userManager.GetRolesAsync(adminUser);
        return Ok(new { 
            Email = adminEmail, 
            RolesInDb = roles, 
            UserObj = new { adminUser.Id, adminUser.UserName }
        });
    }
}

public class AssignRoleRequest
{
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
