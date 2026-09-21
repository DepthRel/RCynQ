namespace RCynQ.Ssh.Keys.HostKeys;

/// <summary>
/// Defines the outcome of a host key verification.
/// </summary>
public enum HostKeyVerificationStatus
{
    /// <summary>
    /// No expected fingerprint is configured.
    /// </summary>
    NotConfigured = 1,

    /// <summary>
    /// A host key matches the expected fingerprint.
    /// </summary>
    Verified = 2,

    /// <summary>
    /// The host presented keys, none of which match.
    /// </summary>
    Mismatch = 3,

    /// <summary>
    /// No host keys could be retrieved.
    /// </summary>
    Unreachable = 4
}
