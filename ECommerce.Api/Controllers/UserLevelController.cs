using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ECommerce.Infrastructure.Data;
using ECommerce.Domain.Entities.UserManagement;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserLevelController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public UserLevelController(ECommerceDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var levels = await _context.UserLevels
            .Include(ul => ul.UserType)
            .OrderBy(ul => ul.UserLevelId)
            .Select(ul => new
            {
                ul.UserLevelId,
                ul.UserTypeId,
                UserTypeName = ul.UserType != null ? ul.UserType.Name : "",
                ul.Name,
                ul.Code,
                ul.IsActive,
                ul.CreatedAt
            })
            .ToListAsync();

        return Ok(levels);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var level = await _context.UserLevels.Include(ul => ul.UserType).FirstOrDefaultAsync(ul => ul.UserLevelId == id);
        if (level == null) return NotFound("User Level not found.");
        return Ok(level);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserLevel level)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        _context.UserLevels.Add(level);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = level.UserLevelId }, level);
    }



    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await _context.UserLevels.FindAsync(id);
        if (existing == null) return NotFound("User Level not found.");

        existing.IsActive = false;
        await _context.SaveChangesAsync();
        return Ok(new { Message = "User Level deleted successfully." });
    }
}
