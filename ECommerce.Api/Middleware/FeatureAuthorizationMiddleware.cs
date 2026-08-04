using System.Net;
using Casbin;
using ECommerce.Api.MetadataHolder;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Api.Middleware;

public class FeatureAuthorizationMiddleware
{
    private readonly RequestDelegate _next;

    public FeatureAuthorizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IEnforcer enforcer)
    {
        Endpoint? endpoint = context.GetEndpoint();
        if (endpoint == null)
        {
            await _next(context);
            return;
        }

        if (endpoint.Metadata.GetMetadata<AllowAnonymousAttribute>() != null)
        {
            await _next(context);
            return;
        }

        var featureMeta = endpoint.Metadata.GetMetadata<FeatureAuthorizationAttribute>();
        if (featureMeta == null)
        {
            await _next(context);
            return;
        }

        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Authentication is required.");
            return;
        }

        var dbContext = context.RequestServices.GetRequiredService<ECommerce.Infrastructure.Data.ECommerceDbContext>();
        var roles = context.User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();
        var email = context.User.FindFirst(ClaimTypes.Email)?.Value ?? context.User.Identity?.Name;
        var userLogin = !string.IsNullOrEmpty(email) 
            ? await dbContext.AppUserLogins.FirstOrDefaultAsync(ul => ul.UserName == email) 
            : null;

        string userLevelId = userLogin != null && userLogin.UserLevelId > 0
            ? userLogin.UserLevelId.ToString()
            : (roles.Contains("Admin") ? "1" : (roles.Contains("SupportAgent") ? "3" : "4"));

        string tenantId = userLogin?.TenantId ?? "global";

        bool isAuthorized = await enforcer.EnforceAsync(userLevelId, tenantId, featureMeta.Feature, featureMeta.Action);

        if (!isAuthorized)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.WriteAsync($"Forbidden: Missing required permission '{featureMeta.Action}' for feature '{featureMeta.Feature}'.");
            return;
        }

        await _next(context);
    }
}
