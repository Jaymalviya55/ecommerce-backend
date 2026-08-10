using Microsoft.AspNetCore.Mvc;
using Casbin;
using Microsoft.EntityFrameworkCore;
using ECommerce.Infrastructure.Data;

namespace ECommerce.Api.Controllers;

public class UserLevelToRoleRequest
{
    public int UserLevelId { get; set; }
    public string UserRoleName { get; set; } = string.Empty;
}

[ApiController]
[Route("api/[controller]")]
public class UserLevelToRoleController : ControllerBase
{
    private readonly IEnforcer _enforcer;
    private readonly ECommerceDbContext _context;

    public UserLevelToRoleController(IEnforcer enforcer, ECommerceDbContext context)
    {
        _enforcer = enforcer;
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllMappings()
    {
        var groupingRules = _enforcer.GetNamedGroupingPolicy("g");
        var userLevels = await _context.UserLevels.ToDictionaryAsync(u => u.UserLevelId.ToString(), u => u.Name);

        var list = groupingRules.Select(rule =>
        {
            var r = rule.ToList();
            string lvlId = r.Count > 0 ? r[0] : "";
            return new
            {
                UserLevelId = lvlId,
                UserLevelName = userLevels.ContainsKey(lvlId) ? userLevels[lvlId] : $"Level {lvlId}",
                UserRoleName = r.Count > 1 ? r[1] : "",
                TenantId = r.Count > 2 ? r[2] : "global"
            };
        });

        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> MapUserLevelToRole([FromBody] UserLevelToRoleRequest request)
    {
        if (request.UserLevelId <= 0 || string.IsNullOrWhiteSpace(request.UserRoleName))
            return BadRequest("UserLevelId and UserRoleName are required.");

        bool added = await _enforcer.AddGroupingPolicyAsync(request.UserLevelId.ToString(), request.UserRoleName, "global");
        if (!added) return BadRequest("Mapping already exists.");

        return Ok(new { Message = $"Successfully mapped UserLevel {request.UserLevelId} to Role '{request.UserRoleName}'." });
    }

    [HttpDelete]
    public async Task<IActionResult> RemoveMapping([FromBody] UserLevelToRoleRequest request)
    {
        bool removed = await _enforcer.RemoveGroupingPolicyAsync(request.UserLevelId.ToString(), request.UserRoleName, "global");
        if (!removed) return NotFound("Mapping not found.");

        return Ok(new { Message = "Mapping removed successfully." });
    }
}
