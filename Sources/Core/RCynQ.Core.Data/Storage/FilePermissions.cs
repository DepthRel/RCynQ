using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RCynQ.Core.Data.Storage;

/// <summary>
/// Restricts files and directories to their owner.
/// </summary>
/// <remarks>
/// On Unix-like systems the modes <c>0700</c>/<c>0600</c> are applied. On Windows the inherited access rules are removed
/// and only the current user and <c>SYSTEM</c> keep access, which is also what OpenSSH requires for private keys.
/// </remarks>
public static class FilePermissions
{
    /// <summary>
    /// Restricts a directory to the owner; on Windows the rules are inherited by files created inside it.
    /// </summary>
    /// <param name="path">The directory path.</param>
    public static void RestrictDirectoryToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictOnWindows(new DirectoryInfo(path));
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// Restricts a file to the owner.
    /// </summary>
    /// <param name="path">The file path.</param>
    public static void RestrictFileToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictOnWindows(new FileInfo(path));
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>
    /// Writes bytes atomically: to a temporary file in the same directory, then moved over the target.
    /// </summary>
    /// <param name="path">The target file path.</param>
    /// <param name="content">The content.</param>
    /// <param name="ownerOnly"><see langword="true"/> to restrict the file to the owner.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public static async Task WriteAtomicallyAsync(string path, byte[] content, bool ownerOnly, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new ArgumentException("Path has no directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, content, cancellationToken);
            if (ownerOnly)
            {
                RestrictFileToOwner(temp);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Replaces the access rules of a file or directory with full control for the current user and <c>SYSTEM</c>.
    /// </summary>
    /// <param name="target">The file or directory.</param>
    [SupportedOSPlatform("windows")]
    private static void RestrictOnWindows(FileSystemInfo target)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is not { } user)
            {
                return;
            }

            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            if (target is DirectoryInfo directory)
            {
                const InheritanceFlags inherited = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inherited, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherited, PropagationFlags.None, AccessControlType.Allow));
                directory.SetAccessControl(security);
            }
            else if (target is FileInfo file)
            {
                var security = new FileSecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
                file.SetAccessControl(security);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException or InvalidOperationException)
        {
            // Best effort: a file system without ACL support keeps its permissions.
        }
    }
}
