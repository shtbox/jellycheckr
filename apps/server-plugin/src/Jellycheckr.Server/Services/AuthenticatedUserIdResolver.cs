using System.Security.Claims;
using Jellycheckr.Server.Infrastructure;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellycheckr.Server.Services;

public interface IAuthenticatedUserIdResolver
{
    string? Resolve(HttpContext? httpContext);
}

public sealed class AuthenticatedUserIdResolver : IAuthenticatedUserIdResolver
{
    private static readonly string[] UserIdClaimTypes =
    {
        ClaimTypes.NameIdentifier,
        "sub",
        "uid",
        "user_id",
        "userId"
    };

    private readonly IAuthService _authService;
    private readonly ILogger<AuthenticatedUserIdResolver> _logger;

    public AuthenticatedUserIdResolver(IAuthService authService, ILogger<AuthenticatedUserIdResolver> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public string? Resolve(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        var principal = httpContext.User;
        foreach (var claimType in UserIdClaimTypes)
        {
            var claimValue = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(claimValue))
            {
                return claimValue;
            }
        }

        try
        {
            var authorizationInfo = _authService.Authenticate(httpContext.Request).GetAwaiter().GetResult();
            var userId = authorizationInfo?.UserId.ToString();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return userId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogJellycheckrDebug(ex, "[Jellycheckr] Failed to resolve authenticated user id from authorization context.");
        }

        return null;
    }
}
