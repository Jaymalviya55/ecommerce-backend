using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ECommerce.Infrastructure.Data;
using ECommerce.Domain.Entities.UserManagement;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserRoleController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public UserRoleController(ECommerceDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _context.AppUserRoles
            .Include(ur => ur.UserLevel)
            .OrderBy(ur => ur.UserRoleId)
            .Select(ur => new
            {
                ur.UserRoleId,
                ur.UserLevelId,
                UserLevelName = ur.UserLevel != null ? ur.UserLevel.Name : "",
                ur.Name,
                ur.Sequence,
                ur.ApplicableToAllSelectedLevelUser,
                ur.IsActive,
                ur.CreatedAt
            })
            .ToListAsync();

        return Ok(roles);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var role = await _context.AppUserRoles.Include(ur => ur.UserLevel).FirstOrDefaultAsync(ur => ur.UserRoleId == id);
        if (role == null) return NotFound("User Role not found.");
        return Ok(role);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserRole role)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        _context.AppUserRoles.Add(role);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = role.UserRoleId }, role);
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var existing = await _context.AppUserRoles.FindAsync(id);
        if (existing == null) return NotFound("User Role not found.");

        existing.IsActive = !existing.IsActive;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await _context.AppUserRoles.FindAsync(id);
        if (existing == null) return NotFound("User Role not found.");

        existing.IsActive = false;
        await _context.SaveChangesAsync();
        return Ok(new { Message = "User Role deleted successfully." });
    }
}
