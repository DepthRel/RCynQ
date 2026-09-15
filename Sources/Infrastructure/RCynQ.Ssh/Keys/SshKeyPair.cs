using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Validation;

namespace RCynQ.Ssh.Keys;

/// <summary>
/// The result of generating an SSH key pair.
/// </summary>
/// <param name="PrivateKeyPath">The private key path.</param>
/// <param name="PublicKeyPath">The public key path.</param>
/// <param name="PublicKey">The parsed public key.</param>
/// <param name="Fingerprint">The SHA-256 fingerprint.</param>
public sealed record SshKeyPair(string PrivateKeyPath, string PublicKeyPath, SshPublicKey PublicKey, string Fingerprint);
