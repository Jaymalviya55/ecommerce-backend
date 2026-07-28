using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ECommerceDbContext _context;

    public ProfileController(UserManager<ApplicationUser> userManager, ECommerceDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }
    private string GetUserId() => User.FindFirst("uid")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
    [HttpGet]
    public async Task<IActionResult> GetProfileInfo()
    {
        var user = await _userManager.FindByIdAsync(GetUserId());
        if (user == null) return NotFound("User not found");

        return Ok(new
        {
            user.FirstName,
            user.LastName,
            user.Gender,
            user.Email,
            user.PhoneNumber
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfileInfo([FromBody] UpdateProfileRequest request)
    {
        var user = await _userManager.FindByIdAsync(GetUserId());
        if (user == null) return NotFound("User not found");

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Gender = request.Gender;
        user.PhoneNumber = request.PhoneNumber;

        var result = await _userManager.UpdateAsync(user);
        if (result.Succeeded) return Ok(new { Message = "Profile updated successfully" });

        return BadRequest(result.Errors);
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> GetAddresses()
    {
        var userId = GetUserId();
        var addresses = await _context.Addresses
            .Where(a => a.UserId == userId)
            .ToListAsync();
            
        return Ok(addresses);
    }

    [HttpPost("addresses")]
    public async Task<IActionResult> AddAddress([FromBody] AddressRequest request)
    {
        var userId = GetUserId();
        
        // If it's the first address, or they selected default, we need to handle default logic
        var existingAddresses = await _context.Addresses.Where(a => a.UserId == userId).ToListAsync();
        bool isFirstAddress = existingAddresses.Count == 0;
        
        if (request.IsDefault || isFirstAddress)
        {
            foreach (var addr in existingAddresses) addr.IsDefault = false;
        }

        var address = new Address
        {
            UserId = userId,
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            AlternatePhone = request.AlternatePhone,
            Pincode = request.Pincode,
            Locality = request.Locality,
            StreetAddress = request.StreetAddress,
            City = request.City,
            State = request.State,
            Landmark = request.Landmark,
            AddressType = request.AddressType,
            IsDefault = request.IsDefault || isFirstAddress
        };

        _context.Addresses.Add(address);
        await _context.SaveChangesAsync();

        return Ok(address);
    }

    [HttpPut("addresses/{id}")]
    public async Task<IActionResult> UpdateAddress(int id, [FromBody] AddressRequest request)
    {
        var userId = GetUserId();
        var address = await _context.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (address == null) return NotFound("Address not found");

        if (request.IsDefault && !address.IsDefault)
        {
            var existingAddresses = await _context.Addresses.Where(a => a.UserId == userId).ToListAsync();
            foreach (var addr in existingAddresses) addr.IsDefault = false;
        }

        address.FullName = request.FullName;
        address.PhoneNumber = request.PhoneNumber;
        address.AlternatePhone = request.AlternatePhone;
        address.Pincode = request.Pincode;
        address.Locality = request.Locality;
        address.StreetAddress = request.StreetAddress;
        address.City = request.City;
        address.State = request.State;
        address.Landmark = request.Landmark;
        address.AddressType = request.AddressType;
        address.IsDefault = request.IsDefault;

        await _context.SaveChangesAsync();
        return Ok(address);
    }

    [HttpDelete("addresses/{id}")]
    public async Task<IActionResult> DeleteAddress(int id)
    {
        var userId = GetUserId();
        var address = await _context.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (address == null) return NotFound("Address not found");

        _context.Addresses.Remove(address);
        await _context.SaveChangesAsync();

        // If we deleted the default, set another one to default if exists
        if (address.IsDefault)
        {
            var nextAddress = await _context.Addresses.FirstOrDefaultAsync(a => a.UserId == userId);
            if (nextAddress != null)
            {
                nextAddress.IsDefault = true;
                await _context.SaveChangesAsync();
            }
        }

        return Ok(new { Message = "Address deleted" });
    }
}

public class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Gender { get; set; }
    public string? PhoneNumber { get; set; }
}

public class AddressRequest
{
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string AlternatePhone { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public string Locality { get; set; } = string.Empty;
    public string StreetAddress { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Landmark { get; set; } = string.Empty;
    public string AddressType { get; set; } = "Home";
    public bool IsDefault { get; set; }
}
