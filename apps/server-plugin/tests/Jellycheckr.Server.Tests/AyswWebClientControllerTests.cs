using System.Security.Claims;
using Jellycheckr.Contracts;
using Jellycheckr.Server.Controllers;
using Jellycheckr.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellycheckr.Server.Tests;

public sealed class AyswWebClientControllerTests
{
    [Fact]
    public void Unregister_ReturnsForbidden_WhenServiceRejectsOwnership()
    {
        var controller = CreateController(new UnauthorizedUnregisterService(), resolvedUserId: "u1");

        var result = controller.Unregister(new WebClientUnregisterRequest { SessionId = "s1" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public void Unregister_ReturnsForbidden_WhenAuthenticatedUserIdMissing()
    {
        var controller = CreateController(new NoopRegistrationService(), resolvedUserId: null);

        var result = controller.Unregister(new WebClientUnregisterRequest { SessionId = "s1" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    private static AyswWebClientController CreateController(IWebClientRegistrationService registrationService, string? resolvedUserId)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));

        var controller = new AyswWebClientController(
            registrationService,
            new StubAuthenticatedUserIdResolver(resolvedUserId),
            NullLogger<AyswWebClientController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal
                }
            }
        };

        return controller;
    }

    private sealed class StubAuthenticatedUserIdResolver : IAuthenticatedUserIdResolver
    {
        private readonly string? _userId;

        public StubAuthenticatedUserIdResolver(string? userId)
        {
            _userId = userId;
        }

        public string? Resolve(HttpContext? httpContext)
        {
            return _userId;
        }
    }

    private sealed class UnauthorizedUnregisterService : IWebClientRegistrationService
    {
        public WebClientRegisterResponse Register(string? userId, WebClientRegisterRequest request) => new() { Registered = false };
        public WebClientHeartbeatResponse Heartbeat(string? userId, WebClientHeartbeatRequest request) => new() { Accepted = false };
        public void Unregister(string? userId, WebClientUnregisterRequest request) => throw new UnauthorizedAccessException("forbidden");
    }

    private sealed class NoopRegistrationService : IWebClientRegistrationService
    {
        public WebClientRegisterResponse Register(string? userId, WebClientRegisterRequest request) => new() { Registered = true };
        public WebClientHeartbeatResponse Heartbeat(string? userId, WebClientHeartbeatRequest request) => new() { Accepted = true };
        public void Unregister(string? userId, WebClientUnregisterRequest request)
        {
        }
    }
}
