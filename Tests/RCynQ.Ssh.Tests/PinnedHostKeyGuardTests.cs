using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Ssh.Tests.Support;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests of the guard that makes the SSH client know a pinned host by its pinned key, and by no other, before a
/// connection is made.
/// </summary>
public sealed class PinnedHostKeyGuardTests : IDisposable
{
    /// <summary>The directory of the keys of the node; its name has a space.</summary>
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq guard tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The <c>known_hosts</c> file of the node.</summary>
    private readonly string _knownHosts;

    /// <summary>The SSH client options of the node.</summary>
    private readonly SshClientOptions _options;

    /// <summary>The Ed25519 host key of the mediator: the one a client negotiates when it has no reason to ask for another.</summary>
    private readonly SshPublicKey _ed25519 = FakeSshHosts.Key("ssh-ed25519", 1);

    /// <summary>The RSA host key of the mediator.</summary>
    private readonly SshPublicKey _rsa = FakeSshHosts.Key("ssh-rsa", 2);

    /// <summary>The ECDSA host key of the mediator.</summary>
    private readonly SshPublicKey _ecdsa = FakeSshHosts.Key("ecdsa-sha2-nistp256", 3);

    /// <summary>The host key of somebody who answers in place of the mediator.</summary>
    private readonly SshPublicKey _impostor = FakeSshHosts.Key("ssh-ed25519", 9);

    /// <summary>The hosts the client can reach.</summary>
    private readonly FakeSshHosts _hosts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PinnedHostKeyGuardTests"/> class.
    /// </summary>
    public PinnedHostKeyGuardTests()
    {
        _knownHosts = Path.Combine(_directory, "known_hosts");
        _options = new SshClientOptions { ConfigFile = Path.Combine(_directory, "ssh_config"), KnownHostsFile = _knownHosts };
        _hosts.Host("relay.example", _ed25519, _ecdsa, _rsa);
    }

    /// <summary>
    /// Verifies the case of a node that was exported by an earlier version: the pinned fingerprint is the one of the RSA
    /// key, the client negotiates Ed25519 by itself and that key is already on record. The pinned key is asked for by
    /// its kind, found by its fingerprint and becomes the only key the host is known by.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pinned_Key_Of_Another_Kind_Than_The_Client_Prefers_Is_Found()
    {
        File.WriteAllLines(_knownHosts, new[] { $"relay.example {_ed25519.Type} {_ed25519.Blob}", $"untouched.example {_impostor.Type} {_impostor.Blob}" });

        await Guard().EnsurePinnedAsync(new[] { Mediator(_rsa.Fingerprint) });

        KnownHostsFile.Find(_knownHosts, "relay.example", 22).Select(key => key.Fingerprint).Should().Equal(_rsa.Fingerprint);
        KnownHostsFile.Find(_knownHosts, "untouched.example", 22).Should().ContainSingle();
        _hosts.Connections.Should().OnlyContain(spec => spec.Arguments.Contains("PreferredAuthentications=none"), "the keys are asked for without logging in");
        Directory.GetFiles(_directory).Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "known_hosts", "ssh_config" }, "the files of the questions are removed");
    }

    /// <summary>
    /// Verifies that every kind of key a host may have is asked for, so a pin of any of them is found.
    /// </summary>
    /// <param name="type">The kind of the pinned key.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("ssh-ed25519")]
    [InlineData("ssh-rsa")]
    [InlineData("ecdsa-sha2-nistp256")]
    public async Task Pin_Of_Any_Kind_Of_Key_Is_Found(string type)
    {
        var pinned = new[] { _ed25519, _rsa, _ecdsa }.Single(key => key.Type == type);

        await Guard().EnsurePinnedAsync(new[] { Mediator(pinned.Fingerprint) });

        KnownHostsFile.Find(_knownHosts, "relay.example", 22).Select(key => key.Fingerprint).Should().Equal(pinned.Fingerprint);
    }

    /// <summary>
    /// Verifies that a host already known by its pinned key and nothing else is not asked again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Host_Known_By_Its_Pinned_Key_Is_Not_Asked_Again()
    {
        var guard = Guard();
        await guard.EnsurePinnedAsync(new[] { Mediator(_rsa.Fingerprint) });
        var asked = _hosts.Connections.Count;

        await guard.EnsurePinnedAsync(new[] { Mediator(_rsa.Fingerprint) });

        _hosts.Connections.Should().HaveCount(asked);
    }

    /// <summary>
    /// Verifies that a key recorded next to the pinned one does not stay trusted: the host is known by the pinned key
    /// only, and no connection is needed to get there.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Other_Keys_Recorded_For_The_Host_Are_Dropped()
    {
        File.WriteAllLines(_knownHosts, new[] { $"relay.example {_impostor.Type} {_impostor.Blob}", $"relay.example {_rsa.Type} {_rsa.Blob}" });

        await Guard().EnsurePinnedAsync(new[] { Mediator(_rsa.Fingerprint) });

        KnownHostsFile.Find(_knownHosts, "relay.example", 22).Select(key => key.Fingerprint).Should().Equal(_rsa.Fingerprint);
        _hosts.Connections.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a host that owns no key with the pinned fingerprint is refused: the message names the pinned
    /// fingerprint and the keys the host presented, and the host is left with no trusted key at all.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Host_Without_The_Pinned_Key_Is_Refused()
    {
        _hosts.Host("relay.example", _impostor);
        File.WriteAllLines(_knownHosts, new[] { $"relay.example {_impostor.Type} {_impostor.Blob}", $"untouched.example {_impostor.Type} {_impostor.Blob}" });

        var ensure = () => Guard().EnsurePinnedAsync(new[] { Mediator(_rsa.Fingerprint) });

        var error = await ensure.Should().ThrowAsync<SshExecutionException>();
        error.Which.Message.Should().Contain("mediator-1").And.Contain(_rsa.Fingerprint).And.Contain(_impostor.Fingerprint);
        KnownHostsFile.Find(_knownHosts, "relay.example", 22).Should().BeEmpty("a key that is not the pinned one must not stay trusted");
        KnownHostsFile.Find(_knownHosts, "untouched.example", 22).Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that a host that does not answer is reported as such, with what the client said, after one attempt
    /// instead of one per kind of key.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Host_That_Does_Not_Answer_Is_Reported_After_One_Attempt()
    {
        var ensure = () => Guard().EnsurePinnedAsync(new[] { Mediator(_rsa.Fingerprint, host: "gone.example") });

        var error = await ensure.Should().ThrowAsync<SshExecutionException>();
        error.Which.Message.Should().Contain("gone.example").And.Contain("Connection timed out");
        _hosts.Connections.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that the hosts of a chain are taken in order: the mediator first, then the host behind it, which is
    /// asked through the mediator, and on its own port.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Host_Behind_A_Mediator_Is_Asked_Through_The_Mediator()
    {
        var destKey = FakeSshHosts.Key("ssh-rsa", 5);
        _hosts.Host("[dest.internal]:2222", FakeSshHosts.Key("ssh-ed25519", 6), destKey);
        var dest = new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "dest.internal", 2222, "rcynq", destKey.Fingerprint);

        await Guard().EnsurePinnedAsync(new[] { Mediator(_ed25519.Fingerprint), dest });

        KnownHostsFile.Find(_knownHosts, "relay.example", 22).Select(key => key.Fingerprint).Should().Equal(_ed25519.Fingerprint);
        KnownHostsFile.Find(_knownHosts, "dest.internal", 2222).Select(key => key.Fingerprint).Should().Equal(destKey.Fingerprint);
        var first = _hosts.Connections[0].Arguments;
        first.Should().NotContain("-J");
        first.Last(argument => argument.Contains('@')).Should().Be("jump@relay.example");
        _hosts.Connections.Skip(1).Should().OnlyContain(spec => spec.Arguments.Contains("-J") && spec.Arguments.Contains("jump@relay.example:22") && spec.Arguments.Contains("rcynq@dest.internal"));
    }

    /// <summary>
    /// Verifies how the question is put: in the directory of the configuration file, with a <c>known_hosts</c> file of
    /// its own given before every other option, so neither the file of the node nor a file of the system answers it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Question_Uses_Its_Own_Known_Hosts_File()
    {
        await Guard().EnsurePinnedAsync(new[] { Mediator(_ed25519.Fingerprint) });

        var spec = _hosts.Connections.Should().ContainSingle().Subject;
        spec.WorkingDirectory.Should().Be(_directory);
        var arguments = spec.Arguments.ToList();
        arguments.Take(2).Should().Equal("-F", "ssh_config");
        var own = arguments.FindIndex(argument => argument.StartsWith("UserKnownHostsFile=", StringComparison.Ordinal));
        arguments[own].Should().NotContain(" ").And.NotContain("/").And.NotContain("\\", "the name is relative to the directory ssh runs in, whose path may have spaces");
        arguments.FindLastIndex(argument => argument.StartsWith("UserKnownHostsFile=", StringComparison.Ordinal)).Should().Be(own, "ssh takes the first value of an option");
        arguments.Should().Contain(argument => argument.StartsWith("GlobalKnownHostsFile=", StringComparison.Ordinal));
        arguments.Should().Contain(new[] { "HostKeyAlgorithms=ssh-ed25519", "StrictHostKeyChecking=accept-new", "BatchMode=yes" });
    }

    /// <summary>
    /// Verifies what is left alone: a peer without a pinned fingerprint, a peer that runs no SSH server, and a node that
    /// has no <c>known_hosts</c> file of its own.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Only_Pinned_Ssh_Peers_Are_Checked()
    {
        File.WriteAllLines(_knownHosts, new[] { $"relay.example {_impostor.Type} {_impostor.Blob}" });
        var direct = new PeerDescriptor(
            new NodeId("dest-1"), "Dest", NodeRole.Dest, "relay.example", 22, "rcynq", _rsa.Fingerprint,
            endpointMode: EndpointMode.Direct, tlsFingerprint: "SHA256:" + new string('A', 64), endpointPort: 18444);
        var withoutFile = new SshClientOptions();

        await Guard().EnsurePinnedAsync(new[] { Mediator(null), direct });
        await new PinnedHostKeyGuard(TestTexts.English, withoutFile, new SshCommandBuilder(withoutFile), _hosts).EnsurePinnedAsync(new[] { Mediator(_rsa.Fingerprint) });

        KnownHostsFile.Find(_knownHosts, "relay.example", 22).Should().ContainSingle("nothing was checked, so nothing was dropped");
        _hosts.Connections.Should().BeEmpty();
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// Creates the guard of the node.
    /// </summary>
    /// <returns>The guard.</returns>
    private PinnedHostKeyGuard Guard()
        => new(TestTexts.English, _options, new SshCommandBuilder(_options, new SshClientConfigFile(TestTexts.English, _options)), _hosts);

    /// <summary>
    /// Creates the mediator peer.
    /// </summary>
    /// <param name="fingerprint">The pinned fingerprint, or <see langword="null"/>.</param>
    /// <param name="host">The address of the mediator.</param>
    /// <returns>The peer.</returns>
    private static PeerDescriptor Mediator(string? fingerprint, string host = "relay.example")
        => new(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, host, 22, "jump", fingerprint);
}
