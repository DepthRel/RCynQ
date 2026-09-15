using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// The environment of the local node collected with operating system tools to prefill an export.
/// </summary>
/// <remarks>
/// Reachability means the port accepted a connection from this machine on that address; it does not prove that a
/// firewall or NAT between the neighbours lets the connection through.
/// </remarks>
/// <param name="MachineName">The machine name.</param>
/// <param name="SshUser">The current operating system user, usable as the SSH user, or an empty string.</param>
/// <param name="SshPort">The port of the local SSH server that was probed.</param>
/// <param name="SshdListening">Whether the local SSH server accepts connections.</param>
/// <param name="ServicePortListening">Whether the RCynQ service port accepts connections.</param>
/// <param name="Addresses">The usable local addresses, best candidates first.</param>
/// <param name="SuggestedHost">The recommended host for the export, or <see langword="null"/>.</param>
/// <param name="SshPublicKey">The SSH public key line of this node, or <see langword="null"/>.</param>
/// <param name="SshPublicKeyPath">The file the public key was read from, or <see langword="null"/>.</param>
/// <param name="SshKeyFingerprint">The SHA-256 fingerprint of the public key, or <see langword="null"/>.</param>
/// <param name="HostKeyFingerprints">The SHA-256 fingerprints of the local SSH server host keys.</param>
/// <param name="Warnings">The problems found.</param>
/// <param name="ProbedAtUtc">The probe time.</param>
public sealed record NodeEnvironmentReport(
    string MachineName,
    string SshUser,
    int SshPort,
    bool SshdListening,
    bool ServicePortListening,
    IReadOnlyList<NetworkAddressInfo> Addresses,
    string? SuggestedHost,
    string? SshPublicKey,
    string? SshPublicKeyPath,
    string? SshKeyFingerprint,
    IReadOnlyList<string> HostKeyFingerprints,
    IReadOnlyList<EnvironmentWarning> Warnings,
    DateTimeOffset ProbedAtUtc);
