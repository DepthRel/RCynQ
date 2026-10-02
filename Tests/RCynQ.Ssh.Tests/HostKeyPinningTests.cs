using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Ssh.Routing;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests of the <c>known_hosts</c> reader, of the order of host key fingerprints and of the hosts a route passes.
/// </summary>
public sealed class HostKeyPinningTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-hostkey-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The <c>known_hosts</c> file of the node.</summary>
    private readonly string _knownHosts;

    /// <summary>The host key of the peer.</summary>
    private readonly SshPublicKey _real = Key("ssh-ed25519", 1);

    /// <summary>A second host key of the same peer, of another kind.</summary>
    private readonly SshPublicKey _realRsa = Key("ssh-rsa", 2);

    /// <summary>The host key of somebody else.</summary>
    private readonly SshPublicKey _other = Key("ssh-ed25519", 3);

    /// <summary>
    /// Initializes a new instance of the <see cref="HostKeyPinningTests"/> class.
    /// </summary>
    public HostKeyPinningTests()
    {
        _knownHosts = Path.Combine(_directory, "known_hosts");
    }

    /// <summary>
    /// Verifies how hosts are named and found: by bare name on port 22, as <c>[host]:port</c> otherwise, in a list of
    /// names, hashed, and never through a marker line.
    /// </summary>
    [Fact]
    public void Known_Hosts_Finds_The_Keys_Of_A_Host()
    {
        File.WriteAllLines(_knownHosts, new[]
        {
            "# a comment",
            $"relay.example,203.0.113.4 {_real.Type} {_real.Blob}",
            $"[dest.internal]:2222 {_realRsa.Type} {_realRsa.Blob} a comment",
            $"{Hashed("hidden.example")} {_other.Type} {_other.Blob}",
            $"@revoked relay.example {_other.Type} {_other.Blob}",
            "relay.example ssh-dss AAAA"
        });

        KnownHostsFile.Find(_knownHosts, "RELAY.example", 22).Select(key => key.Fingerprint).Should().Equal(_real.Fingerprint);
        KnownHostsFile.Find(_knownHosts, "203.0.113.4", 22).Should().ContainSingle();
        KnownHostsFile.Find(_knownHosts, "dest.internal", 2222).Select(key => key.Fingerprint).Should().Equal(_realRsa.Fingerprint);
        KnownHostsFile.Find(_knownHosts, "dest.internal", 22).Should().BeEmpty("the key is recorded for another port");
        KnownHostsFile.Find(_knownHosts, "hidden.example", 22).Select(key => key.Fingerprint).Should().Equal(_other.Fingerprint);
        KnownHostsFile.Find(Path.Combine(_directory, "missing"), "relay.example", 22).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that removing a host leaves the other hosts of a line, the other lines and the marker lines alone.
    /// </summary>
    [Fact]
    public void Known_Hosts_Removes_Only_The_Host()
    {
        File.WriteAllLines(_knownHosts, new[]
        {
            $"relay.example,203.0.113.4 {_real.Type} {_real.Blob}",
            $"relay.example {_realRsa.Type} {_realRsa.Blob}",
            $"other.example {_other.Type} {_other.Blob}",
            $"@cert-authority relay.example {_other.Type} {_other.Blob}"
        });

        KnownHostsFile.Remove(_knownHosts, "relay.example", 22).Should().Be(2);

        KnownHostsFile.Find(_knownHosts, "relay.example", 22).Should().BeEmpty();
        KnownHostsFile.Find(_knownHosts, "203.0.113.4", 22).Should().ContainSingle("the address of the same line keeps its key");
        KnownHostsFile.Find(_knownHosts, "other.example", 22).Should().ContainSingle();
        File.ReadAllText(_knownHosts).Should().Contain("@cert-authority relay.example");
        KnownHostsFile.Remove(_knownHosts, "relay.example", 22).Should().Be(0);
    }

    /// <summary>
    /// Verifies that a host can be made known by one key only: its other records go, the records of other hosts and the
    /// marker lines stay, and a file that does not exist yet is created.
    /// </summary>
    [Fact]
    public void Known_Hosts_Replaces_The_Keys_Of_A_Host()
    {
        File.WriteAllLines(_knownHosts, new[]
        {
            $"relay.example {_real.Type} {_real.Blob}",
            $"other.example {_other.Type} {_other.Blob}",
            $"@cert-authority relay.example {_other.Type} {_other.Blob}"
        });

        KnownHostsFile.Replace(_knownHosts, "relay.example", 22, _realRsa);
        KnownHostsFile.Replace(Path.Combine(_directory, "keys", "new_hosts"), "dest.internal", 2222, _real);

        KnownHostsFile.Find(_knownHosts, "relay.example", 22).Select(key => key.Fingerprint).Should().Equal(_realRsa.Fingerprint);
        KnownHostsFile.Find(_knownHosts, "other.example", 22).Should().ContainSingle();
        File.ReadAllText(_knownHosts).Should().Contain("@cert-authority relay.example");
        File.ReadAllLines(Path.Combine(_directory, "keys", "new_hosts")).Should().Equal($"[dest.internal]:2222 {_real.Type} {_real.Blob}");
    }

    /// <summary>
    /// Verifies that the fingerprints of a host are listed in the order the SSH client prefers the kinds of key, so the
    /// first one, which goes into the export of the node, is the key a client negotiates on its first connection.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Host_Fingerprints_Come_In_The_Order_The_Client_Prefers()
    {
        var ecdsa = Key("ecdsa-sha2-nistp256", 4);
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(new ProcessResult(
            0,
            string.Join('\n', $"relay.example {_realRsa.Type} {_realRsa.Blob}", $"relay.example {ecdsa.Type} {ecdsa.Blob}", $"relay.example {_real.Type} {_real.Blob}"),
            string.Empty));

        var fingerprints = await new HostKeyVerifier(processes, new SshClientOptions()).GetHostFingerprintsAsync(new SshEndpoint("rcynq", "relay.example"));

        fingerprints.Should().Equal(_real.Fingerprint, ecdsa.Fingerprint, _realRsa.Fingerprint);
    }

    /// <summary>
    /// Verifies which peers of a route have their host keys checked: every host an SSH session passes or ends at, and
    /// never the endpoint that is reached on its own TLS port.
    /// </summary>
    [Fact]
    public void Route_Names_The_Peers_An_Ssh_Session_Passes()
    {
        var relay = Peer(_real.Fingerprint);
        var dest = new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq", _realRsa.Fingerprint);
        var direct = new PeerDescriptor(
            new NodeId("dest-1"), "Dest", NodeRole.Dest, "192.168.10.20", 22, "rcynq",
            endpointMode: EndpointMode.Direct, tlsFingerprint: "SHA256:" + new string('A', 64), endpointPort: 18444);

        ChainRoute.Resolve(new[] { Link("dest", dest, 100), Link("relay", relay, 10) }).SshPeers.Select(peer => peer.Id.Value).Should().Equal("mediator-1", "dest-1");
        ChainRoute.Resolve(new[] { Link("dest", direct, 100), Link("relay", relay, 10) }).SshPeers.Select(peer => peer.Id.Value).Should().Equal("mediator-1");
        ChainRoute.Resolve(new[] { Link("dest", direct, 100) }).SshPeers.Should().BeEmpty();
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// Creates a link of a chain.
    /// </summary>
    /// <param name="name">The name of the link.</param>
    /// <param name="peer">The peer.</param>
    /// <param name="priority">The position in the chain.</param>
    /// <returns>The link.</returns>
    private static ChainLinkConfig Link(string name, PeerDescriptor peer, int priority)
        => new(Guid.NewGuid(), name, TransportKind.Direct, peer, 0, 18443, priority);

    /// <summary>
    /// Creates the mediator peer at <c>relay.example</c>.
    /// </summary>
    /// <param name="fingerprint">The pinned fingerprint, or <see langword="null"/>.</param>
    /// <returns>The peer.</returns>
    private static PeerDescriptor Peer(string? fingerprint)
        => new(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "relay.example", 22, "jump", fingerprint);

    /// <summary>
    /// Creates a well-formed public key of a kind; the seed makes keys differ.
    /// </summary>
    /// <param name="type">The kind of key.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The key.</returns>
    private static SshPublicKey Key(string type, byte seed)
    {
        var name = Encoding.ASCII.GetBytes(type);
        var blob = new byte[4 + name.Length + 4 + 32];
        BinaryPrimitives.WriteUInt32BigEndian(blob, (uint)name.Length);
        name.CopyTo(blob, 4);
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(4 + name.Length), 32);
        Array.Fill(blob, seed, 4 + name.Length + 4, 32);
        return new SshPublicKey(type, Convert.ToBase64String(blob), null);
    }

    /// <summary>
    /// Hashes a host name the way <c>HashKnownHosts</c> writes it.
    /// </summary>
    /// <param name="host">The host name.</param>
    /// <returns>The hashed name.</returns>
    private static string Hashed(string host)
    {
        var salt = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
#pragma warning disable CA5350 // The algorithm is fixed by the file format of OpenSSH.
        var hash = HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(host));
#pragma warning restore CA5350
        return $"|1|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)}";
    }
}
