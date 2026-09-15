using RCynQ.Core.Config;

namespace RCynQ.Ssh.Keys.AuthorizedKeys;

/// <summary>
/// Determines which <c>authorized_keys</c> file the local SSH server reads for the current user.
/// </summary>
/// <remarks>
/// OpenSSH for Windows ignores <c>%USERPROFILE%\.ssh\authorized_keys</c> for members of the Administrators group and reads
/// <c>%ProgramData%\ssh\administrators_authorized_keys</c> instead (the default <c>Match Group administrators</c> block of
/// <c>sshd_config</c>).
/// </remarks>
public static class AuthorizedKeysLocation
{
    /// <summary>The Windows file for members of the Administrators group.</summary>
    public const string AdministratorsFileName = "administrators_authorized_keys";

    /// <summary>
    /// Detects the file for the current user.
    /// </summary>
    /// <param name="options">The SSH client options; <see cref="SshClientOptions.AuthorizedKeysPath"/> wins when set.</param>
    /// <returns>The target.</returns>
    public static AuthorizedKeysTarget Detect(SshClientOptions options)
        => Resolve(
            options,
            OperatingSystem.IsWindows(),
            WindowsAccounts.IsAdministratorsMember,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    /// <summary>
    /// Resolves the file from explicit platform facts.
    /// </summary>
    /// <param name="options">The SSH client options.</param>
    /// <param name="isWindows">Whether the platform is Windows.</param>
    /// <param name="isAdministratorsMember">Checks Administrators membership (only called on Windows).</param>
    /// <param name="userProfile">The user profile directory.</param>
    /// <param name="programData">The Windows <c>ProgramData</c> directory.</param>
    /// <returns>The target.</returns>
    public static AuthorizedKeysTarget Resolve(SshClientOptions options, bool isWindows, Func<bool> isAdministratorsMember, string userProfile, string programData)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(isAdministratorsMember);
        if (!string.IsNullOrWhiteSpace(options.AuthorizedKeysPath))
        {
            var path = Path.GetFullPath(options.AuthorizedKeysPath);
            return new AuthorizedKeysTarget(path, isWindows && Path.GetFileName(path).Equals(AdministratorsFileName, StringComparison.OrdinalIgnoreCase));
        }

        if (isWindows && isAdministratorsMember())
        {
            return new AuthorizedKeysTarget(Path.Combine(programData, "ssh", AdministratorsFileName), true);
        }

        return new AuthorizedKeysTarget(Path.Combine(userProfile, ".ssh", "authorized_keys"), false);
    }
}
