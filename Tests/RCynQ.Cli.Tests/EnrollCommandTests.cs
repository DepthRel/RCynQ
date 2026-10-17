using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using FluentAssertions;
using RCynQ.Cli.Commands.Enrollment;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Localization;

namespace RCynQ.Cli.Tests;

/// <summary>
/// Tests of <see cref="EnrollCommand"/>, the forced command of a one-time key: it checks what the SSH server received
/// and hands it to the running service on loopback.
/// </summary>
public sealed class EnrollCommandTests : IDisposable
{
    /// <summary>The temporary folder with the configuration.</summary>
    private readonly string _folder = Directory.CreateTempSubdirectory("rcynq-enroll-cli-").FullName;

    /// <summary>
    /// Verifies that a valid enrollment reaches the service of the configuration on loopback, in the language of the
    /// joining node, and that its answer is printed as it is.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Enrollment_Is_Passed_To_The_Local_Service()
    {
        var handler = new RecordingHandler("{\"accepted\":true,\"message\":\"ok\"}");
        var command = Command(Encode(new EnrollmentRequest(Guid.NewGuid(), "c2VjcmV0", Card(), "de")), handler);
        using var console = new FakeInMemoryConsole();

        await command.ExecuteAsync(console);

        handler.Request!.RequestUri.Should().Be(new Uri("http://127.0.0.1:18555" + ApiRoutes.PEER_ENROLL));
        handler.Request.Headers.AcceptLanguage.Should().ContainSingle().Which.Value.Should().Be("de");
        handler.Body.Should().Contain("\"secret\":\"c2VjcmV0\"");
        console.ReadOutputString().Should().Be("{\"accepted\":true,\"message\":\"ok\"}");
    }

    /// <summary>
    /// Verifies that a login with the key that carries no enrollment, or anything else, ends without asking the service.
    /// </summary>
    /// <param name="original">The command the SSH server received, or <see langword="null"/> for an interactive login.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cat /etc/passwd")]
    [InlineData("bm90LWpzb24")]
    public async Task Anything_But_An_Enrollment_Is_Refused(string? original)
    {
        var handler = new RecordingHandler("{}");
        var command = Command(original, handler);
        using var console = new FakeInMemoryConsole();

        var run = async () => await command.ExecuteAsync(console);

        (await run.Should().ThrowAsync<CommandException>()).Which.ExitCode.Should().Be(2);
        handler.Request.Should().BeNull();
    }

    /// <summary>
    /// Deletes the temporary folder.
    /// </summary>
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>
    /// Creates the command with a configuration whose service listens on port 18555.
    /// </summary>
    /// <param name="original">The command the SSH server received.</param>
    /// <param name="handler">The handler that records the request.</param>
    /// <returns>The command.</returns>
    private EnrollCommand Command(string? original, HttpMessageHandler handler)
    {
        var config = Path.Combine(_folder, "syncbridge.json");
        File.WriteAllText(config, "{\"SyncBridge\":{\"NodeId\":\"relay-1\",\"Role\":\"Relay\",\"ListenPort\":18555}}");
        return new EnrollCommand(new Localizer(LanguageCatalog.LoadEmbedded()), name => name == EnrollCommand.ORIGINAL_COMMAND ? original : null, handler)
        {
            ConfigPath = config
        };
    }

    /// <summary>
    /// Writes an enrollment the way the joining node sends it.
    /// </summary>
    /// <param name="request">The enrollment.</param>
    /// <returns>The Base64url of its JSON.</returns>
    private static string Encode(EnrollmentRequest request)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    /// <summary>
    /// Builds the card of a joining Origin.
    /// </summary>
    /// <returns>The card.</returns>
    private static ExportedNodeConfiguration Card()
        => new(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, new NodeId("origin-1"), "Origin", NodeRole.Origin, null,
            "10.20.0.5", "rcynq", 22, 18443, null, DateTimeOffset.UnixEpoch, ProtocolVersion: ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION);

    /// <summary>
    /// Answers every request with a fixed body and keeps the request.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>The body of the answer.</summary>
        private readonly string _answer;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingHandler"/> class.
        /// </summary>
        /// <param name="answer">The body of the answer.</param>
        public RecordingHandler(string answer) => _answer = answer;

        /// <summary>Gets the last request, or <see langword="null"/>.</summary>
        public HttpRequestMessage? Request { get; private set; }

        /// <summary>Gets the body of the last request.</summary>
        public string? Body { get; private set; }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_answer) };
        }
    }
}
