using System.Text.Json;
using Jellycheckr.Contracts;

namespace Jellycheckr.Server.Tests;

public sealed class ContractSerializationTests
{
    [Fact]
    public void WebClientRegisterResponse_Serializes_WithCamelCasePropertyNames()
    {
        var payload = new WebClientRegisterResponse
        {
            Registered = true,
            SessionId = "s1",
            LeaseExpiresUtc = DateTimeOffset.Parse("2026-03-05T22:58:00Z"),
            Config = new EffectiveConfigResponse
            {
                Enabled = true,
                DeveloperMode = true,
                DeveloperPromptAfterSeconds = 15
            }
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("registered", out _));
        Assert.True(root.TryGetProperty("sessionId", out _));
        Assert.True(root.TryGetProperty("leaseExpiresUtc", out _));
        Assert.True(root.TryGetProperty("config", out var config));
        Assert.True(config.TryGetProperty("enabled", out _));
        Assert.True(config.TryGetProperty("developerMode", out _));
        Assert.True(config.TryGetProperty("developerPromptAfterSeconds", out _));

        Assert.False(root.TryGetProperty("Registered", out _));
        Assert.False(root.TryGetProperty("SessionId", out _));
    }
}
