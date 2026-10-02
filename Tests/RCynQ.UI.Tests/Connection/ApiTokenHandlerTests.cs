using System.Net;
using FluentAssertions;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Connection;
using RCynQ.UI.Tests.Support;

namespace RCynQ.UI.Tests.Connection;

/// <summary>
/// Tests of <see cref="ApiTokenHandler"/> and <see cref="ApiTokenProvider"/>.
/// </summary>
public sealed class ApiTokenHandlerTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-ui-token-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that a token file created after the UI started is picked up, and a rejected token is re-read.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task File_Token_Is_Read_Lazily_And_Refreshed_After_401()
    {
        var tokenFile = Path.Combine(_root, "api.token");
        var tokens = new ApiTokenProvider(new ServiceConnectionSettings(new Uri("http://127.0.0.1:1/"), string.Empty, tokenFile, "syncbridge.json"));
        var inner = new RecordingHttpHandler();
        using var client = new HttpClient(new ApiTokenHandler(TestTexts.Russian, tokens, inner)) { BaseAddress = new Uri("http://127.0.0.1:1/") };

        await client.GetAsync("state");
        File.WriteAllText(tokenFile, "first\n");
        inner.StatusCode = HttpStatusCode.Unauthorized;
        await client.GetAsync("state");
        File.WriteAllText(tokenFile, "second");
        inner.StatusCode = HttpStatusCode.OK;
        await client.GetAsync("state");
        await client.GetAsync("state");

        inner.Tokens.Should().Equal(null, "first", "second", "second");
    }

    /// <summary>
    /// Verifies that an explicitly passed token is never replaced by file contents.
    /// </summary>
    [Fact]
    public void Explicit_Token_Is_Not_Re_Read()
    {
        var tokens = new ApiTokenProvider(new ServiceConnectionSettings(new Uri("http://127.0.0.1:1/"), "explicit", ApiTokenProvider.ARGUMENT_SOURCE, "syncbridge.json"));

        tokens.Invalidate();

        tokens.IsFileBacked.Should().BeFalse();
        tokens.GetToken().Should().Be("explicit");
    }

    /// <summary>
    /// Verifies that a token file containing header-breaking characters is refused instead of being sent.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Tampered_Token_Is_Not_Sent()
    {
        var tokenFile = Path.Combine(_root, "api.token");
        File.WriteAllText(tokenFile, "abc\r\nX-Injected: 1");
        var tokens = new ApiTokenProvider(new ServiceConnectionSettings(new Uri("http://127.0.0.1:1/"), string.Empty, tokenFile, "syncbridge.json"));
        var inner = new RecordingHttpHandler();
        using var client = new HttpClient(new ApiTokenHandler(TestTexts.Russian, tokens, inner)) { BaseAddress = new Uri("http://127.0.0.1:1/") };

        var send = () => client.GetAsync("state");

        await send.Should().ThrowAsync<FormatException>();
        inner.Tokens.Should().BeEmpty();
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
