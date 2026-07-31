using ECommerce.Api.Models;
using ECommerce.Api.Services;
using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using Google.Apis.Auth;
using Casbin;
using ECommerce.Domain.Entities.UserManagement;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ECommerceDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserManager<ApplicationUser> userManager, ECommerceDbContext context, ITokenService tokenService, IConfiguration configuration, IEmailService emailService, ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _context = context;
        _tokenService = tokenService;
        _configuration = configuration;
        _emailService = emailService;
        _logger = logger;
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

        int userTypeId = request.UserTypeId ?? 4;
        int userLevelId = request.UserLevelId ?? 4;

        // Assign Identity Role based on UserLevelId / UserTypeId
        string assignedRole = "Customer";
        if (userLevelId == 1 || userTypeId == 1) assignedRole = "Admin";
        else if (userLevelId == 2 || userTypeId == 2) assignedRole = "FulfillmentStaff";
        else if (userLevelId == 3 || userTypeId == 3) assignedRole = "SupportAgent";

        await _userManager.AddToRoleAsync(user, assignedRole);

        // Connect user to the 4-Tier User Management System (AppUserLogins)
        var userLogin = new UserLogin
        {
            UserName = user.Email,
            PasswordHash = user.PasswordHash ?? "",
            UserTypeId = userTypeId,
            UserLevelId = userLevelId,
            UserReferenceId = user.Id,
            TenantId = "global",
            IsActive = true,
            IsDefaultPasswordChange = false,
            LoginAttemptsCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.AppUserLogins.Add(userLogin);
        await _context.SaveChangesAsync();

        // Generate email confirmation token
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
        var verifyUrl = $"{frontendUrl}/verify-email?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(user.Email)}";
        
        var emailBody = $@"
        <html>
        <head>
            <style>
                .button {{
                    background-color: #4F46E5;
                    border: none;
                    color: white !important;
                    padding: 15px 32px;
                    text-align: center;
                    text-decoration: none;
                    display: inline-block;
                    font-size: 16px;
                    margin: 4px 2px;
                    cursor: pointer;
                    border-radius: 8px;
                    font-family: Arial, sans-serif;
                }}
                .container {{
                    font-family: Arial, sans-serif;
                    padding: 20px;
                    color: #333;
                }}
            </style>
        </head>
        <body>
            <div class='container'>
                <h2>Welcome to Enterprise Store!</h2>
                <p>Hi there,</p>
                <p>Thank you for registering. Please click the button below to verify your email address and activate your account:</p>
                <br/>
                <a href='{verifyUrl}' class='button'>Verify Account</a>
                <br/><br/>
                <p>If the button doesn't work, you can copy and paste this link into your browser:</p>
                <p>{verifyUrl}</p>
                <p>Thanks,<br/>The Enterprise Store Team</p>
            </div>
        </body>
        </html>";

        _logger.LogInformation($"[DEV EMAIL VERIFICATION LINK]: {verifyUrl}");
        Console.WriteLine($"\n=======================================================\n[DEV VERIFICATION LINK FOR {user.Email}]:\n{verifyUrl}\n=======================================================\n");

        try
        {
            await _emailService.SendEmailAsync(user.Email, "Verify your Enterprise Store account", emailBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send verification email to {user.Email}: {ex.Message}");
        }

        return Ok(new 
        { 
            Message = "User registered successfully. Please check your email to verify your account.",
            UserTypeId = userTypeId,
            UserLevelId = userLevelId,
            AssignedRole = assignedRole
        });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
            return BadRequest("Invalid email.");

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        
        if (result.Succeeded)
            return Ok(new { Message = "Email verified successfully. You can now log in." });
            
        return BadRequest("Invalid or expired verification token.");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // 1. Query the 4-Tier UserManagement AppUserLogins table
        var userLogin = await _context.AppUserLogins
            .Include(u => u.UserType)
            .Include(u => u.UserLevel)
            .FirstOrDefaultAsync(u => u.UserName == request.Email || u.UserName == request.Email.Trim());

        ApplicationUser? user = null;
        if (userLogin != null)
        {
            if (!userLogin.IsActive)
                return Unauthorized("Your account has been deactivated by an Administrator.");

            user = await _userManager.FindByIdAsync(userLogin.UserReferenceId);
        }
        
        if (user == null)
        {
            user = await _userManager.FindByEmailAsync(request.Email);
        }
        
        if (user == null)
            return Unauthorized("Invalid email or password.");

        if (!await _userManager.IsEmailConfirmedAsync(user))
            return Unauthorized("Please verify your email address before logging in.");

        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized("Account is locked due to too many failed attempts. Try again later.");

        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        
        if (!isPasswordValid)
        {
            if (userLogin != null)
            {
                userLogin.LoginAttemptsCount++;
                _context.AppUserLogins.Update(userLogin);
                await _context.SaveChangesAsync();
            }

            await _userManager.AccessFailedAsync(user);
            return Unauthorized("Invalid email or password.");
        }

        // Successful login: Update login attempts and reset lockout
        if (userLogin != null)
        {
            userLogin.LoginAttemptsCount = 0;
            _context.AppUserLogins.Update(userLogin);
            await _context.SaveChangesAsync();
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        return await GenerateTokenResponse(user);
    }

    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings()
            {
                Audience = new List<string> { _configuration["Authentication:GoogleClientId"] ?? throw new InvalidOperationException("GoogleClientId is missing") } 
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
            
            var user = await _userManager.FindByEmailAsync(payload.Email);
            
            if (user == null)
            {
                // Register a new user automatically
                user = new ApplicationUser
                {
                    UserName = payload.Email,
                    Email = payload.Email,
                    EmailConfirmed = true // Since Google already verified it
                };
                
                var result = await _userManager.CreateAsync(user);
                if (!result.Succeeded)
                    return BadRequest(result.Errors);
            }
            else
            {
                // If the user already existed but their email wasn't confirmed,
                // logging in with Google proves they own it. Confirm it now!
                if (!user.EmailConfirmed)
                {
                    user.EmailConfirmed = true;
                    await _userManager.UpdateAsync(user);
                }
            }

            // Record that they logged in with Google (wakes up AspNetUserLogins table!)
            var loginInfo = new UserLoginInfo("Google", payload.Subject, "Google");
            var loginExists = await _userManager.FindByLoginAsync("Google", payload.Subject);
            
            if (loginExists == null)
            {
                await _userManager.AddLoginAsync(user, loginInfo);
            }

            // Successful login, reset lockout
            await _userManager.ResetAccessFailedCountAsync(user);

            return await GenerateTokenResponse(user);
        }
        catch (InvalidJwtException)
        {
            return Unauthorized("Invalid Google token.");
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken()
    {
        var accessToken = Request.Cookies["AccessToken"];
        var refreshToken = Request.Cookies["RefreshToken"];

        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            return Unauthorized("Missing tokens.");

        try
        {
            var principal = _tokenService.GetPrincipalFromExpiredToken(accessToken);
            if (principal == null)
                return Unauthorized("Invalid access token");

            var jwtId = principal.Claims.First(x => x.Type == JwtRegisteredClaimNames.Jti).Value;

            var storedRefreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(x => x.Token == refreshToken);

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

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
        {
            // Don't reveal that the user does not exist or is not confirmed
            return Ok(new { Message = "If an account exists for that email, a password reset link has been sent." });
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
        var resetUrl = $"{frontendUrl}/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(user.Email)}";
        
        var emailBody = $@"
        <html>
        <head>
            <style>
                .button {{
                    background-color: #4F46E5;
                    border: none;
                    color: white !important;
                    padding: 15px 32px;
                    text-align: center;
                    text-decoration: none;
                    display: inline-block;
                    font-size: 16px;
                    margin: 4px 2px;
                    cursor: pointer;
                    border-radius: 8px;
                    font-family: Arial, sans-serif;
                }}
                .container {{
                    font-family: Arial, sans-serif;
                    padding: 20px;
                    color: #333;
                }}
            </style>
        </head>
        <body>
            <div class='container'>
                <h2>Reset Your Password</h2>
                <p>Hi there,</p>
                <p>You recently requested to reset your password for your Enterprise Store account. Click the button below to proceed:</p>
                <br/>
                <a href='{resetUrl}' class='button'>Reset Password</a>
                <br/><br/>
                <p>If you did not request a password reset, please ignore this email or reply to let us know. This password reset is only valid for the next 24 hours.</p>
                <p>If the button doesn't work, you can copy and paste this link into your browser:</p>
                <p>{resetUrl}</p>
                <p>Thanks,<br/>The Enterprise Store Team</p>
            </div>
        </body>
        </html>";

        try
        {
            await _emailService.SendEmailAsync(user.Email, "Reset Password - Enterprise Store", emailBody);
        }
        catch (Exception ex)
        {
            // Log the error but still return the success message for security/sandbox mode
            Console.WriteLine($"Failed to send password reset email: {ex.Message}");
        }

        return Ok(new { Message = "If an account exists for that email, a password reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            // Don't reveal that the user does not exist
            return Ok(new { Message = "Password has been reset successfully." });
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        
        if (result.Succeeded)
            return Ok(new { Message = "Password has been reset successfully." });

        return BadRequest(result.Errors);
    }

    private async Task<IActionResult> GenerateTokenResponse(ApplicationUser user)
    {
        var jwtToken = await _tokenService.GenerateAccessToken(user);
        
        var jwtId = new JwtSecurityTokenHandler().ReadJwtToken(jwtToken)
            .Claims.First(x => x.Type == JwtRegisteredClaimNames.Jti).Value;

        var refreshToken = _tokenService.GenerateRefreshToken(user, jwtId);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // Must be true for SameSite=None
            SameSite = SameSiteMode.None, // Required for cross-origin local dev (5173 -> 5000)
            Expires = DateTime.UtcNow.AddDays(7)
        };

        Response.Cookies.Append("AccessToken", jwtToken, cookieOptions);
        Response.Cookies.Append("RefreshToken", refreshToken.Token, cookieOptions);

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new { 
            Message = "Authentication successful",
            User = new {
                Id = user.Id,
                Email = user.Email,
                Roles = roles
            }
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = DateTime.UtcNow.AddDays(-1)
        };

        Response.Cookies.Append("AccessToken", "", cookieOptions);
        Response.Cookies.Append("RefreshToken", "", cookieOptions);

        return Ok(new { Message = "Logged out successfully" });
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

    [Authorize]
    [HttpGet("user-info")]
    public async Task<IActionResult> GetUserInfo([FromServices] Casbin.IEnforcer enforcer)
    {
        var userId = User.FindFirst("uid")?.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name;
        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(r => r.Value).ToList();

        string userLevelId = roles.Contains("Admin") ? "1" : (roles.Contains("SupportAgent") ? "3" : "4");
        string tenantId = "global";

        var groupingPolicies = enforcer.GetGroupingPolicy()
            .Select(g => g.ToList())
            .Where(g => g.Count >= 3 && g.ElementAtOrDefault(0) == userLevelId && g.ElementAtOrDefault(2) == tenantId)
            .Select(g => g.ElementAtOrDefault(1))
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct()
            .ToList();

        var allPolicies = enforcer.GetPolicy()
            .Select(p => p.ToList())
            .Where(p => p.Count >= 4 && groupingPolicies.Contains(p.ElementAtOrDefault(0)!) && p.ElementAtOrDefault(1) == tenantId)
            .Select(p => new { Role = p.ElementAtOrDefault(0)!, Feature = p.ElementAtOrDefault(2)!, Action = p.ElementAtOrDefault(3)! })
            .ToList();

        var permissions = allPolicies
            .GroupBy(p => p.Feature)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Action).Distinct().ToArray()
            );

        var user = !string.IsNullOrEmpty(email) ? await _userManager.FindByEmailAsync(email) : null;

        return Ok(new
        {
            UserId = userId,
            Email = email,
            FullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : email,
            Roles = roles,
            UserLevelId = userLevelId,
            TenantId = tenantId,
            Permissions = permissions
        });
    }
}

public class AssignRoleRequest
{
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class VerifyEmailRequest
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
