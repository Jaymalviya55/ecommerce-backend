using System.Net;
using Casbin;
using ECommerce.Api.MetadataHolder;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

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

        var roles = context.User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();
        string userLevelId = roles.Contains("Admin") ? "1" : (roles.Contains("SupportAgent") ? "3" : "4");
        string tenantId = "global";

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
