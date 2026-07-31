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

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UserRole model)
    {
        var existing = await _context.AppUserRoles.FindAsync(id);
        if (existing == null) return NotFound("User Role not found.");

        existing.UserLevelId = model.UserLevelId;
        existing.Name = model.Name;
        existing.Sequence = model.Sequence;
        existing.ApplicableToAllSelectedLevelUser = model.ApplicableToAllSelectedLevelUser;
        existing.IsActive = model.IsActive;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await _context.AppUserRoles.FindAsync(id);
        if (existing == null) return NotFound("User Role not found.");

        _context.AppUserRoles.Remove(existing);
        await _context.SaveChangesAsync();
        return Ok(new { Message = "User Role deleted successfully." });
    }
}
