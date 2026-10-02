using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Patches;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Git.Paths;
using RCynQ.Service.Tests.Support;
using RCynQ.Sync.Content;
using RCynQ.Sync.Security;
using RCynQ.Sync.Services;
using RCynQ.Sync.Signing;
using RCynQ.Sync.Transfer;

namespace RCynQ.Service.Tests;

/// <summary>
/// End-to-end tests of the patch session: a real <see cref="HttpPatchSender"/> talks to a real Dest service on Kestrel, with real
/// files on both sides. Only the SSH forward is replaced by a direct connection to the loopback port of the service.
/// </summary>
public sealed class PatchDeliveryEndToEndTests : IAsyncLifetime
{
    /// <summary>The Dest service.</summary>
    private ServiceTestHost _host = null!;

    /// <summary>The working tree of Dest (its repository path).</summary>
    private string _destWork = null!;

    /// <summary>The temporary root of the simulated Source.</summary>
    private string _sourceRoot = null!;

    /// <summary>The sync base of Source, the state Source and Dest agreed on.</summary>
    private string _sourceBase = null!;

    /// <summary>The working tree of Source.</summary>
    private string _sourceWork = null!;

    /// <summary>The signer of Source.</summary>
    private readonly EcdsaSignatureService _signer = new(TestTexts.English, new InMemorySecretStore());

    /// <summary>The forward manager, replaced by a direct connection.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>
    /// Starts Dest, registers Source as its peer and creates the same starting tree on both sides and the baseline on Dest.
    /// </summary>
    /// <returns>A task representing the setup.</returns>
    public async Task InitializeAsync()
    {
        _host = await ServiceTestHost.StartAsync(NodeRole.Dest);
        _destWork = Path.Combine(_host.Root, "work");
        _sourceRoot = Path.Combine(Path.GetTempPath(), "rcynq-e2e-tests", Guid.NewGuid().ToString("N"));
        _sourceBase = Directory.CreateDirectory(Path.Combine(_sourceRoot, "base")).FullName;
        _sourceWork = Directory.CreateDirectory(Path.Combine(_sourceRoot, "work")).FullName;
        foreach (var dir in new[] { _destWork, _sourceBase, _sourceWork })
        {
            Write(dir, "a.txt", "v1\n");
            Write(dir, "b.txt", "keep\n");
        }

        using (var scope = _host.Services.CreateScope())
        {
            var key = Convert.ToBase64String(await _signer.ExportPublicKeyAsync());
            await scope.ServiceProvider.GetRequiredService<IPeerRepository>()
                .UpsertAsync(new PeerDescriptor(new NodeId("source-1"), "Source", NodeRole.Source, "source.local", 22, "rcynq", signingPublicKey: key));
        }

        await _host.Services.GetRequiredService<IPatchService>().RebuildBaselineAsync();
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_host.BaseAddress.Port);
    }

    /// <summary>
    /// Stops the service and deletes the files.
    /// </summary>
    /// <returns>A task representing the cleanup.</returns>
    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        try
        {
            Directory.Delete(_sourceRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Verifies that changes, a large file, a deletion and the <c>.gitignore</c> rules travel end to end: ignored files stay behind
    /// and everything else arrives byte for byte.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Patch_Is_Delivered_File_By_File_And_Applied()
    {
        var big = new byte[8 * 1024 * 1024];
        new Random(1234).NextBytes(big);
        Write(_sourceWork, "a.txt", "v2\n");
        File.Delete(Path.Combine(_sourceWork, "b.txt"));
        Write(_sourceWork, "new/n.txt", "new\n");
        File.WriteAllBytes(Path.Combine(_sourceWork, "new", "big.bin"), big);
        Write(_sourceWork, ".gitignore", "*.tmp\nbuild/\n");
        Write(_sourceWork, "junk.tmp", "ignored");
        Write(_sourceWork, "build/out.dll", "ignored directory");

        var envelope = await BuildAsync();
        var result = await CreateSender().SendAsync(envelope, new WorkdirPatchContent(TestTexts.English, _sourceWork, new PathUtility(TestTexts.English)), Chain());

        result.Status.Should().Be(PatchStatus.Applied, result.Error);
        result.AppliedFileCount.Should().Be(5);
        Read(_destWork, "a.txt").Should().Be("v2\n");
        File.Exists(Path.Combine(_destWork, "b.txt")).Should().BeFalse();
        Read(_destWork, "new/n.txt").Should().Be("new\n");
        SHA256.HashData(File.ReadAllBytes(Path.Combine(_destWork, "new", "big.bin"))).Should().Equal(SHA256.HashData(big));
        Read(_destWork, ".gitignore").Should().Be("*.tmp\nbuild/\n");
        File.Exists(Path.Combine(_destWork, "junk.tmp")).Should().BeFalse("ignored files are not sent");
        Directory.Exists(Path.Combine(_destWork, "build")).Should().BeFalse("ignored directories are not sent");
        envelope.Manifest.Select(change => change.Path).Should().BeEquivalentTo(".gitignore", "a.txt", "b.txt", "new/big.bin", "new/n.txt");

        var recent = await _host.Services.GetRequiredService<IPatchService>().GetRecentAsync(10);
        recent.Should().ContainSingle(patch => patch.Id == envelope.Id && patch.Direction == PatchDirection.Incoming && patch.Status == PatchStatus.Applied);
    }

    /// <summary>
    /// Verifies that a file edited between the moment the manifest was built and the moment it is uploaded is refused by the
    /// receiver, because it no longer matches the signed hash, and that nothing is applied.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task File_Edited_After_The_Manifest_Was_Built_Is_Refused_And_Nothing_Is_Applied()
    {
        Write(_sourceWork, "a.txt", "v2\n");
        Write(_sourceWork, "new/n.txt", "new\n");
        var envelope = await BuildAsync();
        Write(_sourceWork, "new/n.txt", "edited while the patch was on its way\n");

        var send = () => CreateSender().SendAsync(envelope, new WorkdirPatchContent(TestTexts.English, _sourceWork, new PathUtility(TestTexts.English)), Chain());

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("Malformed");
        Read(_destWork, "a.txt").Should().Be("v1\n", "the patch is applied all or nothing");
        File.Exists(Path.Combine(_destWork, "new", "n.txt")).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a wrong link token is refused before any file leaves Source.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Wrong_Link_Token_Is_Refused()
    {
        Write(_sourceWork, "a.txt", "v2\n");
        var envelope = await BuildAsync();
        var sender = CreateSender("not-the-token-of-dest");

        var send = () => sender.SendAsync(envelope, new WorkdirPatchContent(TestTexts.English, _sourceWork, new PathUtility(TestTexts.English)), Chain());

        (await send.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("InvalidToken").And.Contain("401");
        Read(_destWork, "a.txt").Should().Be("v1\n");
    }

    /// <summary>
    /// Builds and signs the envelope from the Source base to the Source working tree.
    /// </summary>
    /// <returns>The signed envelope.</returns>
    private async Task<PatchEnvelope> BuildAsync()
    {
        var builder = new DefaultPatchBuilder(new DefaultFileManifestBuilder(new SnapshotFilter()), new DefaultStateHasher(), TimeProvider.System);
        var envelope = await builder.BuildAsync(new NodeId("source-1"), new NodeId("dest-1"), _sourceBase, _sourceWork);
        return envelope.WithSignature(await _signer.SignAsync(PayloadCanonicalizer.Build(envelope)));
    }

    /// <summary>
    /// Creates the sender of the simulated Source.
    /// </summary>
    /// <param name="token">The link token to present.</param>
    /// <returns>The sender.</returns>
    private HttpPatchSender CreateSender(string token = ServiceTestHost.LinkToken)
        => new(
            TestTexts.English,
            new SocketsHttpHandler { UseProxy = false },
            _forwards,
            _signer,
            new PatchEnvelopeSerializer(TestTexts.English, new PathUtility(TestTexts.English)),
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = token }, new InMemorySecretStore()),
            new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, _sourceWork),
            new HttpPatchSenderOptions { RetryDelay = TimeSpan.Zero });

    /// <summary>
    /// Creates the chain from Source to Dest.
    /// </summary>
    /// <returns>The chain.</returns>
    private IReadOnlyList<ChainLinkConfig> Chain()
        => new[]
        {
            new ChainLinkConfig(
                Guid.NewGuid(), "dest", TransportKind.Direct,
                new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq"), 0, _host.BaseAddress.Port, 1000)
        };

    /// <summary>
    /// Writes a text file below a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="relative">The relative path.</param>
    /// <param name="content">The content.</param>
    private static void Write(string directory, string relative, string content)
    {
        var full = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>
    /// Reads a text file below a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="relative">The relative path.</param>
    /// <returns>The content.</returns>
    private static string Read(string directory, string relative) => File.ReadAllText(Path.Combine(directory, relative));
}
