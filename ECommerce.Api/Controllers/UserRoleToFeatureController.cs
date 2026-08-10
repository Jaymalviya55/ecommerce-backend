using Microsoft.AspNetCore.Mvc;
using Casbin;

namespace ECommerce.Api.Controllers;

public class UserRoleToFeatureRequest
{
    public string UserRoleName { get; set; } = string.Empty;
    public string FeatureKey { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "read", "write", "delete", "*"
}

[ApiController]
[Route("api/[controller]")]
public class UserRoleToFeatureController : ControllerBase
{
    private readonly IEnforcer _enforcer;

    public UserRoleToFeatureController(IEnforcer enforcer)
    {
        _enforcer = enforcer;
    }

    [HttpGet]
    public IActionResult GetAllPolicies()
    {
        var policies = _enforcer.GetNamedPolicy("p");
        var list = policies.Select(rule =>
        {
            var r = rule.ToList();
            return new
            {
                UserRoleName = r.Count > 0 ? r[0] : "",
                TenantId = r.Count > 1 ? r[1] : "global",
                FeatureKey = r.Count > 2 ? r[2] : "",
                Action = r.Count > 3 ? r[3] : "*"
            };
        });

        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> AddPolicy([FromBody] UserRoleToFeatureRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserRoleName) || string.IsNullOrWhiteSpace(request.FeatureKey) || string.IsNullOrWhiteSpace(request.Action))
            return BadRequest("UserRoleName, FeatureKey, and Action are required.");

        bool added = await _enforcer.AddPolicyAsync(request.UserRoleName, "global", request.FeatureKey, request.Action);
        if (!added) return BadRequest("Policy already exists.");

        return Ok(new { Message = $"Successfully granted '{request.Action}' right on feature '{request.FeatureKey}' to Role '{request.UserRoleName}'." });
    }

    [HttpDelete]
    public async Task<IActionResult> RemovePolicy([FromBody] UserRoleToFeatureRequest request)
    {
        bool removed = await _enforcer.RemovePolicyAsync(request.UserRoleName, "global", request.FeatureKey, request.Action);
        if (!removed) return NotFound("Policy not found.");

        return Ok(new { Message = "Policy removed successfully." });
    }
}
