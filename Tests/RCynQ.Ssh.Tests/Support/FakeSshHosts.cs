using System.Buffers.Binary;
using System.Text;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Ssh.Keys;

namespace RCynQ.Ssh.Tests.Support;

/// <summary>
/// Stands in for the <c>ssh</c> client and the hosts it talks to, as far as the host keys go: a host presents the key
/// of the first kind the client offers, and a client that accepts new keys writes it to its <c>known_hosts</c> file.
/// </summary>
/// <remarks>
/// This is the behaviour of OpenSSH the host key check is built on, observed against a real server: the client
/// negotiates one kind of key, records that key and no other, and on that first connection does not learn the other
/// keys of the host (<c>UpdateHostKeys</c> is disabled for a host that was not known).
/// </remarks>
public sealed class FakeSshHosts : IProcessRunner
{
    /// <summary>The keys of the hosts, by the name a <c>known_hosts</c> file uses.</summary>
    private readonly Dictionary<string, List<SshPublicKey>> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the connections made, in order.
    /// </summary>
    public List<ProcessSpec> Connections { get; } = new();

    /// <summary>
    /// Adds a host with its keys.
    /// </summary>
    /// <param name="knownHostsName">The name of the host in a <c>known_hosts</c> file (<c>host</c> or <c>[host]:port</c>).</param>
    /// <param name="keys">The host keys.</param>
    /// <returns>This instance.</returns>
    public FakeSshHosts Host(string knownHostsName, params SshPublicKey[] keys)
    {
        _hosts[knownHostsName] = keys.ToList();
        return this;
    }

    /// <summary>
    /// Creates a well-formed public key of a kind; the seed makes keys differ.
    /// </summary>
    /// <param name="type">The kind of key.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The key.</returns>
    public static SshPublicKey Key(string type, byte seed)
    {
        var name = Encoding.ASCII.GetBytes(type);
        var blob = new byte[4 + name.Length + 4 + 32];
        BinaryPrimitives.WriteUInt32BigEndian(blob, (uint)name.Length);
        name.CopyTo(blob, 4);
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(4 + name.Length), 32);
        Array.Fill(blob, seed, 4 + name.Length + 4, 32);
        return new SshPublicKey(type, Convert.ToBase64String(blob), null);
    }

    /// <inheritdoc />
    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        Connections.Add(spec);
        var arguments = spec.Arguments;
        var destination = arguments[arguments.ToList().IndexOf("--") + 1];
        var host = destination[(destination.IndexOf('@') + 1)..];
        var port = arguments[arguments.ToList().IndexOf("-p") + 1];
        var name = port == "22" ? host : $"[{host}]:{port}";
        if (!_hosts.TryGetValue(name, out var keys))
        {
            return Task.FromResult(new ProcessResult(255, string.Empty, $"ssh: connect to host {host} port {port}: Connection timed out"));
        }

        var offered = Option(arguments, "HostKeyAlgorithms")?.Split(',') ?? ["ssh-ed25519", "ecdsa-sha2-nistp256", "rsa-sha2-512"];
        var presented = offered.Select(algorithm => keys.FirstOrDefault(key => Signs(key, algorithm))).FirstOrDefault(key => key is not null);
        if (presented is null)
        {
            return Task.FromResult(new ProcessResult(
                255,
                string.Empty,
                $"Unable to negotiate with {host} port {port}: no matching host key type found. Their offer: {string.Join(',', keys.Select(key => key.Type))}"));
        }

        if (Option(arguments, "UserKnownHostsFile") is { } knownHosts)
        {
            File.AppendAllLines(Path.Combine(spec.WorkingDirectory ?? string.Empty, knownHosts), [$"{name} {presented.Type} {presented.Blob}"]);
        }

        return Task.FromResult(new ProcessResult(255, string.Empty, $"{destination}: Permission denied (publickey)."));
    }

    /// <inheritdoc />
    public IRunningProcess Start(ProcessSpec spec) => throw new NotSupportedException("The fake hosts only answer connections that end.");

    /// <summary>
    /// Reads the value of an <c>-o Name=value</c> option.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The name of the option.</param>
    /// <returns>The first value given, as <c>ssh</c> takes it, or <see langword="null"/>.</returns>
    private static string? Option(IReadOnlyList<string> arguments, string name)
        => arguments.FirstOrDefault(argument => argument.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

    /// <summary>
    /// Tells whether a key can sign for a host key algorithm.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="algorithm">The algorithm the client offers.</param>
    /// <returns><see langword="true"/> when the host can answer with the key.</returns>
    private static bool Signs(SshPublicKey key, string algorithm)
        => key.Type == "ssh-rsa" ? algorithm is "rsa-sha2-512" or "rsa-sha2-256" : key.Type == algorithm;
}
