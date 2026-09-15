using FluentAssertions;
using RCynQ.UI.Services.Connection;
using Xunit;

namespace RCynQ.UI.Tests.Connection;

/// <summary>
/// Tests of <see cref="ServiceConnectionResolver"/>.
/// </summary>
public sealed class ServiceConnectionResolverTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-ui-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that the port and the token file are derived from the node configuration.
    /// </summary>
    [Fact]
    public void Derives_Port_And_Token_From_Config()
    {
        var dataRoot = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(dataRoot, "keys"));
        File.WriteAllText(Path.Combine(dataRoot, "keys", "api.token"), "  secret-token \n");
        var config = Path.Combine(_root, "syncbridge.json");
        File.WriteAllText(config, $$"""
            {
              // comments are allowed
              "SyncBridge": { "NodeId": "dev-1", "ListenPort": 19001, "DataRoot": {{System.Text.Json.JsonSerializer.Serialize(dataRoot)}} }
            }
            """);

        var settings = ServiceConnectionResolver.Resolve(new[] { "--config", config }, _ => null);

        settings.ServiceUri.Should().Be(new Uri("http://127.0.0.1:19001/"));
        settings.ApiToken.Should().Be("secret-token");
    }

    /// <summary>
    /// Verifies that explicit arguments win over the environment and configuration.
    /// </summary>
    [Fact]
    public void Arguments_Take_Precedence()
    {
        var environment = new Dictionary<string, string?> { ["RCYNQ_SERVICE_URL"] = "http://127.0.0.1:1", ["RCYNQ_API_TOKEN"] = "env-token" };

        var settings = ServiceConnectionResolver.Resolve(new[] { "--service", "http://127.0.0.1:2", "--token", "arg-token" }, name => environment.GetValueOrDefault(name));

        settings.ServiceUri.Should().Be(new Uri("http://127.0.0.1:2/"));
        settings.ApiToken.Should().Be("arg-token");
    }

    /// <summary>
    /// Verifies that a malformed configuration file falls back to defaults.
    /// </summary>
    [Fact]
    public void Malformed_Config_Uses_Defaults()
    {
        var config = Path.Combine(_root, "broken.json");
        File.WriteAllText(config, "{ not json");

        var settings = ServiceConnectionResolver.Resolve(new[] { "--config", config, "--token-file", Path.Combine(_root, "missing.token") }, _ => null);

        settings.ServiceUri.Should().Be(new Uri("http://127.0.0.1:18443/"));
        settings.ApiToken.Should().BeEmpty();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
