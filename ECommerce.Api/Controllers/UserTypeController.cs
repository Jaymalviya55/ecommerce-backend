using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ECommerce.Infrastructure.Data;
using ECommerce.Domain.Entities.UserManagement;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserTypeController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public UserTypeController(ECommerceDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var types = await _context.UserTypes.OrderBy(u => u.UserTypeId).ToListAsync();
        return Ok(types);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var type = await _context.UserTypes.FindAsync(id);
        if (type == null) return NotFound("User Type not found.");
        return Ok(type);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserType type)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        _context.UserTypes.Add(type);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = type.UserTypeId }, type);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UserType model)
    {
        var existing = await _context.UserTypes.FindAsync(id);
        if (existing == null) return NotFound("User Type not found.");

        existing.Name = model.Name;
        existing.Code = model.Code;
        existing.IsActive = model.IsActive;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }


}
