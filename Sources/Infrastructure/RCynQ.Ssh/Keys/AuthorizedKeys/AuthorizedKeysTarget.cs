namespace RCynQ.Ssh.Keys.AuthorizedKeys;

/// <summary>
/// The <c>authorized_keys</c> file the local SSH server reads for the current user.
/// </summary>
/// <param name="FilePath">The file path.</param>
/// <param name="IsAdministratorsFile">
/// Whether it is the shared Windows file for members of the Administrators group, which only an elevated process may write.
/// </param>
public sealed record AuthorizedKeysTarget(string FilePath, bool IsAdministratorsFile);
