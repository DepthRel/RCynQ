using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Repository;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Security;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests for the two ends of the bundle pull: the service that answers on Dest and the client that asks from Source.
/// </summary>
public sealed class GitBundleTests : IDisposable
{
    /// <summary>The shared link token.</summary>
    private const string Token = "bundle-tests-link-token-0123456789";

    /// <summary>A commit name.</summary>
    private static readonly string Commit = new('a', 40);

    /// <summary>The temporary root.</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rcynq-bundle-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The repository service mock.</summary>
    private readonly IGitRepositoryService _git = Substitute.For<IGitRepositoryService>();

    /// <summary>The peer repository mock.</summary>
    private readonly IPeerRepository _peers = Substitute.For<IPeerRepository>();

    /// <summary>The forward manager mock.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitBundleTests"/> class: a Dest node with a repository that knows one Source peer.
    /// </summary>
    public GitBundleTests()
    {
        Directory.CreateDirectory(_root);
        _git.IsWorkTreeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _git.GetCurrentBranchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("main");
        _git.FilterExistingCommitsAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<string>>().Where(commit => commit == Commit).ToArray());
        _git.CreateBundleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                File.WriteAllText(call.ArgAt<string>(1), "bundle bytes");
                return true;
            });
        _peers.GetAsync(new NodeId("source-1"), Arg.Any<CancellationToken>())
            .Returns(new PeerDescriptor(new NodeId("source-1"), "Source", NodeRole.Source, "source.local", 22, "rcynq"));
        _peers.GetAsync(new NodeId("other-dest"), Arg.Any<CancellationToken>())
            .Returns(new PeerDescriptor(new NodeId("other-dest"), "Other", NodeRole.Dest, "other.local", 22, "rcynq"));
    }

    /// <summary>
    /// Verifies that the service names only known commits to Git, streams from a temporary file and deletes it when done.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Service_Creates_The_Bundle_From_Known_Commits_And_Deletes_It_When_Done()
    {
        var service = CreateService();

        await using var bundle = (await service.CreateAsync(new[] { Commit, new string('b', 40), "not a commit" }, Context()))!;

        bundle.Head.Should().Be("main");
        File.ReadAllText(bundle.Path).Should().Be("bundle bytes");
        await _git.Received(1).CreateBundleAsync(Arg.Any<string>(), bundle.Path, Arg.Is<IReadOnlyCollection<string>>(have => have.Single() == Commit), Arg.Any<CancellationToken>());
        var path = bundle.Path;
        await bundle.DisposeAsync();
        File.Exists(path).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that nothing is returned when the peer has everything.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Service_Returns_Nothing_When_The_Peer_Is_Up_To_Date()
    {
        _git.CreateBundleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(false);

        (await CreateService().CreateAsync(new[] { Commit }, Context())).Should().BeNull();
    }

    /// <summary>
    /// Verifies that the request is refused for a node that is not Dest, a wrong token and a peer that is not a known Source, and that
    /// a Dest without a repository reports it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Service_Authorizes_Role_Token_And_Peer()
    {
        var source = new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, _root);
        (await Reason(() => CreateService(source).CreateAsync(Array.Empty<string>(), Context()))).Should().Be(PatchRejectionReason.RoleNotAllowed);
        (await Reason(() => CreateService().CreateAsync(Array.Empty<string>(), new PatchReceiveContext("source-1", "wrong")))).Should().Be(PatchRejectionReason.InvalidToken);
        (await Reason(() => CreateService().CreateAsync(Array.Empty<string>(), new PatchReceiveContext("stranger", Token)))).Should().Be(PatchRejectionReason.UnknownSender);
        (await Reason(() => CreateService().CreateAsync(Array.Empty<string>(), new PatchReceiveContext("other-dest", Token)))).Should().Be(PatchRejectionReason.UnknownSender, "only a Source may read the repository");
        (await Reason(() => CreateService().CreateAsync(Array.Empty<string>(), new PatchReceiveContext(null, Token)))).Should().Be(PatchRejectionReason.UnknownSender);

        _git.IsWorkTreeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var noRepository = () => CreateService().CreateAsync(Array.Empty<string>(), Context());
        await noRepository.Should().ThrowAsync<SyncBridgeException>();
    }

    /// <summary>
    /// Verifies that a failure while building the bundle leaves no temporary file behind.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Service_Deletes_The_Temporary_File_When_Git_Fails()
    {
        string? created = null;
        _git.CreateBundleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(call =>
            {
                created = call.ArgAt<string>(1);
                File.WriteAllText(created, "partial");
                throw new GitExecutionException("git bundle create failed", 128);
            });

        var create = () => CreateService().CreateAsync(Array.Empty<string>(), Context());

        await create.Should().ThrowAsync<GitExecutionException>();
        created.Should().NotBeNull();
        File.Exists(created!).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the client sends what it has, stores the answer in a file and reads the branch of Dest from the header.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Client_Downloads_The_Bundle_And_Names_What_It_Has()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("# v2 git bundle\n...")) };
            response.Headers.Add(ApiHeaders.GitHead, "release");
            return response;
        });

        var download = await CreateClient(handler).DownloadAsync(Chain(), new[] { Commit }, _root);

        download.Should().NotBeNull();
        download!.Head.Should().Be("release");
        File.ReadAllText(download.Path).Should().StartWith("# v2 git bundle");
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Should().Be(new Uri("http://127.0.0.1:45555/git/bundle"));
        request.Headers[ApiHeaders.NodeId].Should().Be("source-1");
        request.Headers[ApiHeaders.LinkToken].Should().Be(Token);
        JsonSerializer.Deserialize<GitBundleRequest>(request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Have.Should().Equal(Commit);
        await _forwards.Received(1).StopAsync(45555, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that an answer without content means up to date and leaves no file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Client_Reports_Up_To_Date_As_Nothing_To_Download()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        (await CreateClient(handler).DownloadAsync(Chain(), Array.Empty<string>(), _root)).Should().BeNull();
        Directory.GetFiles(_root, "*.bundle").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the errors: a refusal with its reason, a peer of an older version, a lost connection and a body that breaks off.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Client_Reports_Refusals_Old_Peers_And_Broken_Connections()
    {
        var refused = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(JsonSerializer.Serialize(new PatchRejectionResponse(PatchRejectionReason.InvalidToken, "bad token"), new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        });
        var older = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var unreachable = new FakeHandler(_ => throw new HttpRequestException("unreachable"));

        var refusal = () => CreateClient(refused).DownloadAsync(Chain(), Array.Empty<string>(), _root);
        var oldPeer = () => CreateClient(older).DownloadAsync(Chain(), Array.Empty<string>(), _root);
        var lost = () => CreateClient(unreachable).DownloadAsync(Chain(), Array.Empty<string>(), _root);

        (await refusal.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("InvalidToken").And.Contain("401");
        (await oldPeer.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("older version");
        (await lost.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("unreachable");
        Directory.GetFiles(_root, "*.bundle").Should().BeEmpty("a failed download leaves no partial file");
        await _forwards.Received(3).StopAsync(45555, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Deletes the temporary files.
    /// </summary>
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

    /// <summary>
    /// Creates the request context of a valid Source.
    /// </summary>
    /// <returns>The context.</returns>
    private static PatchReceiveContext Context() => new("source-1", Token);

    /// <summary>
    /// Creates the service of a Dest node.
    /// </summary>
    /// <param name="node">The node, or <see langword="null"/> for a Dest with a repository.</param>
    /// <returns>The service.</returns>
    private GitBundleService CreateService(NodeConfig? node = null)
    {
        var appData = Substitute.For<IAppDataFactory>();
        appData.Create().Returns(AppDataPaths.FromRoot(Path.Combine(_root, "data")));
        return new GitBundleService(
            node ?? new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, _root, null),
            _peers,
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = Token }, new InMemorySecretStore()),
            _git,
            appData);
    }

    /// <summary>
    /// Creates the client of a Source node whose HTTP traffic goes through a handler.
    /// </summary>
    /// <param name="handler">The HTTP handler.</param>
    /// <returns>The client.</returns>
    private GitBundleClient CreateClient(HttpMessageHandler handler)
    {
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(45555);
        return new GitBundleClient(
            new PeerLinkFactory(handler, _forwards),
            new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, _root),
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = Token }, new InMemorySecretStore()));
    }

    /// <summary>
    /// Creates a chain from Source through one mediator to a Dest that has an SSH server.
    /// </summary>
    /// <returns>The chain.</returns>
    private static IReadOnlyList<ChainLinkConfig> Chain()
        => new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "hop", TransportKind.Direct, new PeerDescriptor(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "relay.example", 22, "jump"), 0, 22, 0),
            new ChainLinkConfig(Guid.NewGuid(), "endpoint", TransportKind.Direct, new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq"), 0, 18443, 1)
        };

    /// <summary>
    /// Runs an action and returns the rejection reason.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>The reason.</returns>
    private static async Task<PatchRejectionReason> Reason(Func<Task> action)
        => (await action.Should().ThrowAsync<PatchRejectedException>()).Which.Reason;

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
