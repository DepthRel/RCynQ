using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Forwarding;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Ssh.Network;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests for local forwards, key generation, authorized_keys management and host key verification.
/// </summary>
public sealed class ForwardAndKeyTests : IDisposable
{
    /// <summary>
    /// A real ed25519 public key produced by ssh-keygen, with its fingerprint reported by <c>ssh-keygen -lf</c>.
    /// </summary>
    private const string PublicKeyLine = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIE2mfktackE6lnW8Hp/QiIkF7Dp5hFWxNzR5c9wXgKja rcynq test";

    /// <summary>
    /// The fingerprint of <see cref="PublicKeyLine"/>.
    /// </summary>
    private const string Fingerprint = "SHA256:2oxko38oV8xCbRbLhx6eOhTUPEU77VxVHI3VTFa7Xf8";

    /// <summary>
    /// A temporary directory.
    /// </summary>
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "rcynq-ssh-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The target endpoint.
    /// </summary>
    private readonly SshEndpoint _dest = new("rcynq", "dest.internal");

    /// <summary>
    /// Verifies that a forward is registered once its port accepts connections and can be stopped.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LocalForward_Starts_When_Port_Ready_And_Stops()
    {
        var process = Substitute.For<IRunningProcess>();
        process.HasExited.Returns(false);
        var processes = Substitute.For<IProcessRunner>();
        processes.Start(Arg.Any<ProcessSpec>()).Returns(process);
        var probe = Substitute.For<ITcpProbe>();
        // Free before ssh starts, not yet forwarded on the first look, then ready.
        probe.CanConnectAsync("127.0.0.1", 45000, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false, false, true);
        var ports = Substitute.For<IPortAllocator>();
        ports.AllocateLoopbackPort().Returns(45000);
        var manager = new ProcessLocalForwardManager(TestTexts.English, processes, probe, ports, new SshCommandBuilder(new SshClientOptions()), new SshClientOptions());

        var port = await manager.StartAsync(_dest, Array.Empty<SshEndpoint>(), 0, "127.0.0.1", 18443);

        port.Should().Be(45000);
        manager.IsRunning(45000).Should().BeTrue();
        processes.Received(1).Start(Arg.Is<ProcessSpec>(spec => spec.Arguments.Contains("127.0.0.1:45000:127.0.0.1:18443") && spec.Timeout == null));

        await manager.StopAsync(45000);
        process.Received(1).Kill();
        manager.IsRunning(45000).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a forward whose process exits reports ssh's error output.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LocalForward_Reports_Early_Exit()
    {
        var process = Substitute.For<IRunningProcess>();
        process.HasExited.Returns(true);
        process.ExitCode.Returns(255);
        process.StandardError.Returns("Permission denied (publickey).");
        var processes = Substitute.For<IProcessRunner>();
        processes.Start(Arg.Any<ProcessSpec>()).Returns(process);
        var manager = new ProcessLocalForwardManager(TestTexts.English, processes, Substitute.For<ITcpProbe>(), Substitute.For<IPortAllocator>(), new SshCommandBuilder(new SshClientOptions()), new SshClientOptions());

        var start = () => manager.StartAsync(_dest, Array.Empty<SshEndpoint>(), 40000, "127.0.0.1", 18443);

        var error = await start.Should().ThrowAsync<SshExecutionException>();
        error.Which.Message.Should().Contain("Permission denied");
        error.Which.ExitCode.Should().Be(255);
        process.Received().Dispose();
    }

    /// <summary>
    /// Verifies that no forward is started on a port another program already listens on: that program would answer the
    /// readiness check, and the requests meant for the peer would reach it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LocalForward_Refuses_A_Port_That_Is_Already_In_Use()
    {
        var processes = Substitute.For<IProcessRunner>();
        var probe = Substitute.For<ITcpProbe>();
        probe.CanConnectAsync("127.0.0.1", 40001, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
        var manager = new ProcessLocalForwardManager(TestTexts.English, processes, probe, Substitute.For<IPortAllocator>(), new SshCommandBuilder(new SshClientOptions()), new SshClientOptions());

        var start = () => manager.StartAsync(_dest, Array.Empty<SshEndpoint>(), 40001, "127.0.0.1", 18443);

        await start.Should().ThrowAsync<SshExecutionException>();
        processes.DidNotReceiveWithAnyArgs().Start(default!);
        manager.IsRunning(40001).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a forward that ended by itself is released when its port is used again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LocalForward_Releases_An_Ended_Forward_Before_Reusing_Its_Port()
    {
        var first = Substitute.For<IRunningProcess>();
        var second = Substitute.For<IRunningProcess>();
        var processes = Substitute.For<IProcessRunner>();
        processes.Start(Arg.Any<ProcessSpec>()).Returns(first, second);
        var probe = Substitute.For<ITcpProbe>();
        probe.CanConnectAsync("127.0.0.1", 40002, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false, true, false, true);
        var manager = new ProcessLocalForwardManager(TestTexts.English, processes, probe, Substitute.For<IPortAllocator>(), new SshCommandBuilder(new SshClientOptions()), new SshClientOptions());
        await manager.StartAsync(_dest, Array.Empty<SshEndpoint>(), 40002, "127.0.0.1", 18443);
        first.HasExited.Returns(true);

        await manager.StartAsync(_dest, Array.Empty<SshEndpoint>(), 40002, "127.0.0.1", 18443);

        first.Received(1).Dispose();
        manager.IsRunning(40002).Should().BeTrue();
        await manager.StopAsync(40002);
        second.Received(1).Kill();
    }

    /// <summary>
    /// Verifies public key parsing against a real ssh-keygen fingerprint.
    /// </summary>
    [Fact]
    public void PublicKey_Fingerprint_Matches_OpenSsh()
    {
        var key = SshPublicKey.Parse(PublicKeyLine);
        key.Type.Should().Be("ssh-ed25519");
        key.Comment.Should().Be("rcynq test");
        key.Fingerprint.Should().Be(Fingerprint);
        SshPublicKey.NormalizeFingerprint("2oxko38oV8xCbRbLhx6eOhTUPEU77VxVHI3VTFa7Xf8=").Should().Be(Fingerprint);

        var wrongType = () => SshPublicKey.Parse("ssh-rsa AAAAC3NzaC1lZDI1NTE5AAAAIE2mfktackE6lnW8Hp/QiIkF7Dp5hFWxNzR5c9wXgKja");
        var garbage = () => SshPublicKey.Parse("ssh-ed25519 not-base64!");
        wrongType.Should().Throw<FormatException>();
        garbage.Should().Throw<FormatException>();
    }

    /// <summary>
    /// Verifies the exact restricted lines for every key purpose.
    /// </summary>
    [Fact]
    public void AuthorizedKeys_Lines_Are_Restricted()
    {
        var key = SshPublicKey.Parse(PublicKeyLine);
        var blob = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIE2mfktackE6lnW8Hp/QiIkF7Dp5hFWxNzR5c9wXgKja";

        AuthorizedKeysManager.BuildLine(AuthorizedKeyPurpose.GitReadOnly, key, "/srv/repos/app")
            .Should().Be($"restrict,command=\"git-upload-pack '/srv/repos/app'\",no-pty,no-agent-forwarding,no-port-forwarding,no-x11-forwarding {blob} rcynq:GitReadOnly");
        AuthorizedKeysManager.BuildLine(AuthorizedKeyPurpose.PatchForward, key, "127.0.0.1:18443")
            .Should().Be($"restrict,port-forwarding,permitopen=\"127.0.0.1:18443\",command=\"exit 1\",no-pty,no-agent-forwarding,no-x11-forwarding {blob} rcynq:PatchForward");
        AuthorizedKeysManager.BuildLine(AuthorizedKeyPurpose.MediatorForward, key, "next-hop.internal:22")
            .Should().Contain("permitopen=\"next-hop.internal:22\"").And.NotContain("git-upload-pack");
        AuthorizedKeysManager.BuildLine(AuthorizedKeyPurpose.GitReadOnly, key, "C:\\repos\\app").Should().Contain("git-upload-pack 'C:/repos/app'");
    }

    /// <summary>
    /// Verifies rejection of injection attempts and role mismatches.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
    /// <param name="target">The unsafe target.</param>
    [Theory]
    [InlineData(AuthorizedKeyPurpose.GitReadOnly, "/srv/repo'; rm -rf /; '")]
    [InlineData(AuthorizedKeyPurpose.GitReadOnly, "relative/repo")]
    [InlineData(AuthorizedKeyPurpose.GitReadOnly, "/srv/repo\",no-restrict")]
    [InlineData(AuthorizedKeyPurpose.PatchForward, "127.0.0.1")]
    [InlineData(AuthorizedKeyPurpose.PatchForward, "127.0.0.1:99999")]
    [InlineData(AuthorizedKeyPurpose.MediatorForward, "evil\",command=\"sh:22")]
    public void AuthorizedKeys_Rejects_Unsafe_Targets(AuthorizedKeyPurpose purpose, string target)
    {
        var build = () => AuthorizedKeysManager.BuildLine(purpose, SshPublicKey.Parse(PublicKeyLine), target);
        build.Should().Throw<ArgumentException>();

        var gitOnMediator = () => AuthorizedKeysManager.BuildLineForRole(NodeRole.Mediator, SshPublicKey.Parse(PublicKeyLine), "/srv/repo", AuthorizedKeyPurpose.GitReadOnly);
        gitOnMediator.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies safe writing, preservation of existing lines and duplicate prevention.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AuthorizedKeys_Add_Is_Safe_And_Idempotent()
    {
        var path = Path.Combine(_temp, ".ssh", "authorized_keys");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "# existing\nssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPBzgtLuBSnvmhyPyE/aR3fT7G5Ynge9cP0KZC9vUVUb other\n");
        var manager = new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = path });
        var key = SshPublicKey.Parse(PublicKeyLine);

        (await manager.AddAsync(AuthorizedKeyPurpose.PatchForward, key, "127.0.0.1:18443")).Should().BeTrue();
        (await manager.AddAsync(AuthorizedKeyPurpose.GitReadOnly, key, "/srv/repo")).Should().BeFalse("the same key blob is already authorized");

        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(3);
        lines[0].Should().Be("# existing");
        Directory.GetFiles(Path.GetDirectoryName(path)!).Should().ContainSingle();

        var parsed = await manager.ReadAsync();
        parsed.InvalidLines.Should().BeEmpty();
        parsed.Entries.Should().HaveCount(2);
        parsed.Entries[0].IsRestricted.Should().BeFalse();
        parsed.Entries[1].IsRestricted.Should().BeTrue();
        parsed.Entries[1].Key.Fingerprint.Should().Be(Fingerprint);

        AuthorizedKeysManager.Parse(new[] { "garbage line", "restrict ssh-ed25519" }).InvalidLines.Should().Equal(1, 2);
    }

    /// <summary>
    /// Verifies key generation with a mocked ssh-keygen.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task KeyGenerator_Generates_And_Reports_Fingerprint()
    {
        var keyPath = Path.Combine(_temp, "keys", "node_ed25519");
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.Arguments.Contains("-t")), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            File.WriteAllText(keyPath, "PRIVATE");
            File.WriteAllText(keyPath + ".pub", PublicKeyLine + "\n");
            return new ProcessResult(0, "", "");
        });
        processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.Arguments.Contains("-l")), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, $"256 {Fingerprint} rcynq test (ED25519)\n", ""));
        var generator = new SshKeyGenerator(TestTexts.English, processes, new SshClientOptions());

        var pair = await generator.GenerateAsync(keyPath, "rcynq test");

        pair.Fingerprint.Should().Be(Fingerprint);
        pair.PublicKeyPath.Should().Be(keyPath + ".pub");
        await processes.Received(1).RunAsync(
            Arg.Is<ProcessSpec>(spec => spec.FileName == "ssh-keygen" && spec.Arguments.SequenceEqual(new[] { "-t", "ed25519", "-N", "", "-C", "rcynq test", "-f", Path.GetFullPath(keyPath), "-q" })),
            Arg.Any<CancellationToken>());

        var again = () => generator.GenerateAsync(keyPath, "rcynq test");
        await again.Should().ThrowAsync<SyncBridgeException>();
        var quote = () => generator.GenerateAsync(Path.Combine(_temp, "other"), "bad\"comment");
        await quote.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies host key verification outcomes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task HostKeyVerifier_Detects_Mismatch()
    {
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(
            new ProcessResult(0, "# dest.internal:22 SSH-2.0-OpenSSH\ndest.internal " + PublicKeyLine.Replace(" rcynq test", "") + "\n", ""),
            new ProcessResult(0, "dest.internal " + PublicKeyLine.Replace(" rcynq test", "") + "\n", ""),
            new ProcessResult(1, "", "connect timed out"));
        var verifier = new HostKeyVerifier(processes, new SshClientOptions());

        (await verifier.VerifyAsync(Peer(Fingerprint))).Status.Should().Be(HostKeyVerificationStatus.Verified);
        var mismatch = await verifier.VerifyAsync(Peer("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
        mismatch.Status.Should().Be(HostKeyVerificationStatus.Mismatch);
        mismatch.Actual.Should().Equal(Fingerprint);
        (await verifier.VerifyAsync(Peer(Fingerprint))).Status.Should().Be(HostKeyVerificationStatus.Unreachable);
        (await verifier.VerifyAsync(Peer(null))).Status.Should().Be(HostKeyVerificationStatus.NotConfigured);
        HostKeyVerifier.FingerprintsMatch(Fingerprint, "2oxko38oV8xCbRbLhx6eOhTUPEU77VxVHI3VTFa7Xf8").Should().BeTrue();
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    /// <summary>
    /// Creates a Dest peer with the given expected fingerprint.
    /// </summary>
    /// <param name="fingerprint">The expected fingerprint.</param>
    /// <returns>The peer.</returns>
    private static PeerDescriptor Peer(string? fingerprint)
        => new(new NodeId("dest"), "dest", NodeRole.Dest, "dest.internal", 22, "rcynq", fingerprint);
}
