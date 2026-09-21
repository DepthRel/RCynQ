using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Sync.Security;
using RCynQ.Sync.Signing;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Tests.Fixtures;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for <see cref="HttpPatchSender"/> with a mocked forward manager and HTTP handler.
/// </summary>
public sealed class HttpPatchSenderTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The forward manager mock.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpPatchSenderTests"/> class.
    /// </summary>
    public HttpPatchSenderTests()
    {
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), 0, "127.0.0.1", 18443, Arg.Any<CancellationToken>()).Returns(45555);
        SyncFixture.Write(_fixture.SourceWork, "a.txt", "v2\n");
    }

    /// <summary>
    /// Verifies the forward, the request and the signed body.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Sends_Signed_Envelope_Through_Forward()
    {
        var envelope = await _fixture.CreateBuilder().BuildAsync(_fixture.SourceNode.Id, _fixture.DestNode.Id, _fixture.SourceBaseForBuild, _fixture.SourceWork);
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, PatchApplyResult.Applied(envelope.Id, 1)));

        var result = await CreateSender(handler).SendAsync(envelope, _fixture.CreateChain());

        result.Status.Should().Be(PatchStatus.Applied);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Should().Be(new Uri("http://127.0.0.1:45555/patch"));
        request.Headers[HttpPatchSender.NodeIdHeader].Should().Be("source-1");
        request.Headers[HttpPatchSender.LinkTokenHeader].Should().Be(SyncFixture.Token);

        var sent = new PatchEnvelopeSerializer(_fixture.Paths).Deserialize(request.Body);
        sent.IsSigned.Should().BeTrue();
        _fixture.SourceSigner.Verify(PayloadCanonicalizer.Build(sent), sent.Signature, await _fixture.SourceSigner.ExportPublicKeyAsync()).Should().BeTrue();

        await _forwards.Received(1).StartAsync(
            Arg.Is<SshEndpoint>(target => target.Destination == "rcynq@dest.internal"),
            Arg.Is<IReadOnlyList<SshEndpoint>>(jumps => jumps.Single().Destination == "jump@relay.example"),
            0, "127.0.0.1", 18443, Arg.Any<CancellationToken>());
        await _forwards.Received(1).StopAsync(45555, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies the single retry on network failures.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Retries_Once_On_Network_Failure()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1 ? throw new HttpRequestException("connection reset") : Json(HttpStatusCode.OK, PatchApplyResult.Applied(envelope.Id, 1)));

        (await CreateSender(handler).SendAsync(envelope, _fixture.CreateChain())).Status.Should().Be(PatchStatus.Applied);

        handler.Requests.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that two network failures raise an error and still stop the forward.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Gives_Up_After_Retry()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var handler = new FakeHandler(_ => throw new HttpRequestException("unreachable"));

        var send = () => CreateSender(handler).SendAsync(envelope, _fixture.CreateChain());

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("could not be delivered");
        handler.Requests.Should().HaveCount(2);
        await _forwards.Received(1).StopAsync(45555, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a refusal is reported with its reason and not retried.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refusal_Is_Reported()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Forbidden, new PatchRejectionResponse(PatchRejectionReason.InvalidToken, "bad token")));

        var send = () => CreateSender(handler).SendAsync(envelope, _fixture.CreateChain());

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("InvalidToken").And.Contain("403");
        handler.Requests.Should().ContainSingle();
        await _forwards.Received(1).StopAsync(45555, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a replay refusal after a retry means the first attempt was delivered.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Replay_After_Retry_Is_Treated_As_Delivered()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1
            ? throw new TaskCanceledException("response lost")
            : Json(HttpStatusCode.Conflict, new PatchRejectionResponse(PatchRejectionReason.Replay, "already processed")));

        var result = await CreateSender(handler).SendAsync(envelope, _fixture.CreateChain());

        result.Status.Should().Be(PatchStatus.Pending);
        result.Error.Should().Contain("already received");
    }

    /// <summary>
    /// Verifies validation of the envelope against the chain.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Rejects_Envelope_For_Other_Target()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync(new NodeId("somebody-else"));

        var send = () => CreateSender(new FakeHandler(_ => throw new InvalidOperationException())).SendAsync(envelope, _fixture.CreateChain());

        await send.Should().ThrowAsync<ArgumentException>();
        await _forwards.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default, default!, default, default);
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates a JSON response.
    /// </summary>
    /// <param name="status">The status code.</param>
    /// <param name="body">The body.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage Json(HttpStatusCode status, object body)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };

    /// <summary>
    /// Creates the sender.
    /// </summary>
    /// <param name="handler">The HTTP handler.</param>
    /// <returns>The sender.</returns>
    private HttpPatchSender CreateSender(HttpMessageHandler handler)
        => new(
            handler,
            _forwards,
            _fixture.SourceSigner,
            new PatchEnvelopeSerializer(_fixture.Paths),
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = SyncFixture.Token }, new InMemorySecretStore()),
            _fixture.SourceNode,
            new HttpPatchSenderOptions { RetryDelay = TimeSpan.Zero });

    /// <summary>
    /// A captured HTTP request.
    /// </summary>
    /// <param name="Uri">The request URI.</param>
    /// <param name="Headers">The request headers.</param>
    /// <param name="Body">The request body.</param>
    private sealed record CapturedRequest(Uri Uri, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    /// <summary>
    /// An HTTP handler that captures requests and returns scripted responses.
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        /// <summary>The response factory.</summary>
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeHandler"/> class.
        /// </summary>
        /// <param name="respond">The response factory; it may throw to simulate failures.</param>
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        /// <summary>Gets the captured requests.</summary>
        public List<CapturedRequest> Requests { get; } = new();

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!, request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value)), body));
            return _respond(request);
        }
    }
}
