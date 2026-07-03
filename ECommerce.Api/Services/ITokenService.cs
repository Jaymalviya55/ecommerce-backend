using System.Security.Claims;
using ECommerce.Domain.Entities;

namespace ECommerce.Api.Services;

public interface ITokenService
{
    string GenerateAccessToken(ApplicationUser user);
    RefreshToken GenerateRefreshToken(ApplicationUser user, string jwtId);
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
