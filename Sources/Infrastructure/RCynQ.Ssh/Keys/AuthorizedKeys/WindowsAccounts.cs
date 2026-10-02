using System.Diagnostics;
using System.Security.Principal;

namespace RCynQ.Ssh.Keys.AuthorizedKeys;

/// <summary>
/// Queries the Windows account of the current process.
/// </summary>
public static class WindowsAccounts
{
    /// <summary>The well-known SID of the built-in Administrators group.</summary>
    public const string ADMINISTRATORS_SID = "S-1-5-32-544";

    /// <summary>The cached group membership.</summary>
    private static readonly Lazy<bool> _membership = new(QueryAdministratorsMembership, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Checks whether the user belongs to the Administrators group, also when the process is not elevated (the Windows
    /// SSH server then reads <c>administrators_authorized_keys</c> for this user).
    /// </summary>
    /// <returns><see langword="true"/> for members of Administrators on Windows.</returns>
    public static bool IsAdministratorsMember() => OperatingSystem.IsWindows() && _membership.Value;

    /// <summary>
    /// Checks whether the process runs elevated (with administrator rights).
    /// </summary>
    /// <returns><see langword="true"/> for an elevated process on Windows.</returns>
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Checks whether <c>whoami /groups</c> output lists the Administrators group, enabled or deny-only.
    /// </summary>
    /// <param name="whoamiOutput">The output of <c>whoami /groups /fo csv /nh</c>.</param>
    /// <returns><see langword="true"/> when the group is listed.</returns>
    public static bool ListsAdministrators(string? whoamiOutput)
        => whoamiOutput is not null && whoamiOutput.Contains(ADMINISTRATORS_SID, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <c>whoami</c>, which reports deny-only groups that <see cref="WindowsIdentity.Groups"/> leaves out.
    /// </summary>
    /// <returns><see langword="true"/> for members of Administrators.</returns>
    private static bool QueryAdministratorsMembership()
    {
        try
        {
            var info = new ProcessStartInfo("whoami")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("/groups");
            info.ArgumentList.Add("/fo");
            info.ArgumentList.Add("csv");
            info.ArgumentList.Add("/nh");
            using var process = Process.Start(info);
            if (process is null)
            {
                return IsElevated();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return ListsAdministrators(output) || IsElevated();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return IsElevated();
        }
    }
}
