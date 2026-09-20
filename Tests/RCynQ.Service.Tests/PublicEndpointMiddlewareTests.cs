using FluentAssertions;
using Microsoft.AspNetCore.Http;
using RCynQ.Service.Security;
using Xunit;

namespace RCynQ.Service.Tests;

/// <summary>
/// Tests of <see cref="PublicEndpointMiddleware"/>.
/// </summary>
public sealed class PublicEndpointMiddlewareTests
{
    /// <summary>The port of the direct endpoint.</summary>
    private const int PublicPort = 18444;

    /// <summary>The loopback port of the local API.</summary>
    private const int LocalPort = 18443;

    /// <summary>
    /// Verifies that the network listener serves only the two routes a neighbour needs.
    /// </summary>
    /// <param name="method">The request method.</param>
    /// <param name="path">The request path.</param>
    /// <param name="expected">Whether the request reaches the rest of the pipeline.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("POST", "/patch", true)]
    [InlineData("POST", "/PATCH", true)]
    [InlineData("GET", "/health", true)]
    [InlineData("GET", "/patch", false)]
    [InlineData("POST", "/patches/send", false)]
    [InlineData("GET", "/state", false)]
    [InlineData("GET", "/config", false)]
    [InlineData("POST", "/config/import", false)]
    [InlineData("GET", "/hub/sync", false)]
    [InlineData("POST", "/ssh/authorized-keys", false)]
    [InlineData("POST", "/service/shutdown", false)]
    public async Task Public_Port_Serves_Only_Patch_And_Health(string method, string path, bool expected)
    {
        var reached = await InvokeAsync(PublicPort, method, path);

        reached.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that requests on the loopback port are left to the API token middleware.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Local_Port_Is_Not_Restricted()
    {
        (await InvokeAsync(LocalPort, "GET", "/state")).Should().BeTrue();
        (await InvokeAsync(LocalPort, "POST", "/service/shutdown")).Should().BeTrue();
    }

    /// <summary>
    /// Runs the middleware for one request.
    /// </summary>
    /// <param name="port">The local port the request arrived on.</param>
    /// <param name="method">The request method.</param>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true"/> when the request reached the next middleware.</returns>
    private static async Task<bool> InvokeAsync(int port, string method, string path)
    {
        var reached = false;
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = port;
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        var middleware = new PublicEndpointMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            new PublicEndpointOptions(PublicPort));
        await middleware.InvokeAsync(context);

        if (!reached)
        {
            context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        }

        return reached;
    }
}
