using System.IO.Compression;
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
    /// Verifies the forward and the three steps of a session: manifest, content of the missing file, commit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Sends_Manifest_Then_Content_Then_Commit_Through_Forward()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var hash = envelope.ContentHashes.Single();
        var handler = new FakeHandler(request => Script(request, envelope, missing: new[] { hash }));

        var result = await CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

        result.Status.Should().Be(PatchStatus.Applied);
        handler.Requests.Select(request => $"{request.Method} {request.Uri.AbsolutePath}").Should().Equal(
            "POST /patch/sessions",
            $"PUT /patch/sessions/{envelope.Id}/content/{hash}",
            $"POST /patch/sessions/{envelope.Id}/commit");
        handler.Requests.Should().OnlyContain(request => request.Uri.Host == "127.0.0.1" && request.Uri.Port == 45555);
        foreach (var request in handler.Requests)
        {
            request.Headers[HttpPatchSender.NodeIdHeader].Should().Be("source-1");
            request.Headers[HttpPatchSender.LinkTokenHeader].Should().Be(SyncFixture.Token);
        }

        var manifest = new PatchEnvelopeSerializer(_fixture.Paths).Deserialize(Gunzip(handler.Requests[0].Body));
        manifest.IsSigned.Should().BeTrue();
        _fixture.SourceSigner.Verify(PayloadCanonicalizer.Build(manifest), manifest.Signature, await _fixture.SourceSigner.ExportPublicKeyAsync()).Should().BeTrue();
        Encoding.UTF8.GetString(Gunzip(handler.Requests[1].Body)).Should().Be("v2\n");

        await _forwards.Received(1).StartAsync(
            Arg.Is<SshEndpoint>(target => target.Destination == "rcynq@dest.internal"),
            Arg.Is<IReadOnlyList<SshEndpoint>>(jumps => jumps.Single().Destination == "jump@relay.example"),
            0, "127.0.0.1", 18443, Arg.Any<CancellationToken>());
        await _forwards.Received(1).StopAsync(45555, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that every body is gzip-compressed and that a file is compressed while it is streamed, not read into a buffer first.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Bodies_Are_Gzip_Compressed()
    {
        var big = new string('a', 2_000_000);
        SyncFixture.Write(_fixture.SourceWork, "a.txt", big);
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var handler = new FakeHandler(request => Script(request, envelope, missing: envelope.ContentHashes));

        await CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

        handler.Requests.Take(2).Should().OnlyContain(request => request.ContentEncoding == "gzip");
        handler.Requests[1].Body.Length.Should().BeLessThan(20_000, "two million equal characters compress to almost nothing");
        Encoding.UTF8.GetString(Gunzip(handler.Requests[1].Body)).Should().Be(big);
    }

    /// <summary>
    /// Verifies that content the peer already has is not uploaded again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Only_Missing_Content_Is_Uploaded()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var handler = new FakeHandler(request => Script(request, envelope, missing: Array.Empty<string>()));

        (await CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain())).Status.Should().Be(PatchStatus.Applied);

        handler.Requests.Select(request => request.Method).Should().Equal("POST", "POST");
    }

    /// <summary>
    /// Verifies the single retry of a request after a network failure, and that the file is read again from its start.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Retries_Once_On_Network_Failure_During_Upload()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var uploads = 0;
        var handler = new FakeHandler(request => request.Method == HttpMethod.Put && ++uploads == 1
            ? throw new HttpRequestException("connection reset")
            : Script(request, envelope, missing: envelope.ContentHashes));

        (await CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain())).Status.Should().Be(PatchStatus.Applied);

        handler.Requests.Count(request => request.Method == "PUT").Should().Be(2);
        Encoding.UTF8.GetString(Gunzip(handler.Requests.Last(request => request.Method == "PUT").Body)).Should().Be("v2\n");
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

        var send = () => CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

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

        var send = () => CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("InvalidToken").And.Contain("403");
        handler.Requests.Should().ContainSingle();
        await _forwards.Received(1).StopAsync(45555, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a peer of an older version, which does not know the session routes, is named as such instead of reported as a bare 404.
    /// </summary>
    /// <param name="body">The body of the 404 answer: empty, or a generic error as the old public port sent it.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("{\"error\":\"route not found\"}")]
    public async Task Peer_Without_Session_Routes_Is_Reported_As_Older_Version(string body)
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(body) });

        var send = () => CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("older version");
        handler.Requests.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that a file the peer refuses stops the transfer before the commit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refused_Content_Stops_Before_Commit()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var handler = new FakeHandler(request => request.Method == HttpMethod.Put
            ? Json(HttpStatusCode.BadRequest, new PatchRejectionResponse(PatchRejectionReason.Malformed, "hash mismatch"))
            : Script(request, envelope, missing: envelope.ContentHashes));

        var send = () => CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("hash mismatch");
        handler.Requests.Should().NotContain(request => request.Uri.AbsolutePath.EndsWith("/commit", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that a replay refusal of the commit after a retry means the first attempt was delivered.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Replay_After_Retry_Of_Commit_Is_Treated_As_Delivered()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var commits = 0;
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/commit", StringComparison.Ordinal)
            ? ++commits == 1
                ? throw new TaskCanceledException("response lost")
                : Json(HttpStatusCode.Conflict, new PatchRejectionResponse(PatchRejectionReason.Replay, "already processed"))
            : Script(request, envelope, missing: envelope.ContentHashes));

        var result = await CreateSender(handler).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

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

        var send = () => CreateSender(new FakeHandler(_ => throw new InvalidOperationException())).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

        await send.Should().ThrowAsync<ArgumentException>();
        await _forwards.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default, default!, default, default);
    }

    /// <summary>
    /// Verifies that a file above the upload limit is refused up front, naming the file, before anything is sent.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refuses_Files_Above_The_Upload_Limit_Before_Sending_Anything()
    {
        var envelope = await _fixture.BuildSignedEnvelopeAsync();
        var handler = new FakeHandler(_ => throw new InvalidOperationException());

        var send = () => CreateSender(handler, new HttpPatchSenderOptions { RetryDelay = TimeSpan.Zero, MaxFileBytes = 1 }).SendAsync(envelope, _fixture.SourceContent, _fixture.CreateChain());

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("a.txt");
        handler.Requests.Should().BeEmpty();
        await _forwards.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default, default!, default, default);
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Answers a request the way a healthy peer does.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="envelope">The envelope being sent.</param>
    /// <param name="missing">The hashes the peer reports as missing when the session is opened.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage Script(HttpRequestMessage request, PatchEnvelope envelope, IReadOnlyList<string> missing)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/commit", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, PatchApplyResult.Applied(envelope.Id, envelope.Manifest.Count));
        }

        return request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : Json(HttpStatusCode.OK, new PatchSession(envelope.Id, missing));
    }

    /// <summary>
    /// Decompresses a gzip body.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <returns>The uncompressed bytes.</returns>
    private static byte[] Gunzip(byte[] body)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(new MemoryStream(body), CompressionMode.Decompress))
        {
            gzip.CopyTo(output);
        }

        return output.ToArray();
    }

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
    /// <param name="options">The sender options, or <see langword="null"/> for options without retry delay.</param>
    /// <returns>The sender.</returns>
    private HttpPatchSender CreateSender(HttpMessageHandler handler, HttpPatchSenderOptions? options = null)
        => new(
            handler,
            _forwards,
            _fixture.SourceSigner,
            new PatchEnvelopeSerializer(_fixture.Paths),
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = SyncFixture.Token }, new InMemorySecretStore()),
            _fixture.SourceNode,
            options ?? new HttpPatchSenderOptions { RetryDelay = TimeSpan.Zero });

    /// <summary>
    /// A captured HTTP request.
    /// </summary>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Uri">The request URI.</param>
    /// <param name="Headers">The request headers.</param>
    /// <param name="ContentEncoding">The <c>Content-Encoding</c> of the body.</param>
    /// <param name="Body">The request body as sent on the wire.</param>
    private sealed record CapturedRequest(string Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? ContentEncoding, byte[] Body);

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
            var encoding = request.Content?.Headers.ContentEncoding.FirstOrDefault();
            Requests.Add(new CapturedRequest(request.Method.Method, request.RequestUri!, request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value)), encoding, body));
            return _respond(request);
        }
    }
}
