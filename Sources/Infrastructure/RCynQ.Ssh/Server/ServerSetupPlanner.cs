using System.Globalization;
using System.Text;
using RCynQ.Ssh.Enums;

namespace RCynQ.Ssh.Server;

/// <summary>
/// Plans the commands that install and start the SSH server, or open the TLS port of a node in the firewall, for the
/// system at hand. The commands are fixed text of the program; the only value put into them is a port number.
/// </summary>
public static class ServerSetupPlanner
{
    /// <summary>
    /// The exit code of the Windows script when Windows cannot download OpenSSH (error <c>0x800f0954</c>: no access to
    /// Windows Update, typical of a computer in a closed network).
    /// </summary>
    public const int WINDOWS_UPDATE_MISSING = 3;

    /// <summary>The SSH port the firewall opens for the SSH server.</summary>
    private const int SSH_PORT = 22;

    /// <summary>The PowerShell that installs, starts and opens the SSH server of Windows.</summary>
    private const string WINDOWS_INSTALL = """
        $ErrorActionPreference = 'Stop'
        try {
            $capability = Get-WindowsCapability -Online -Name 'OpenSSH.Server*' | Select-Object -First 1
            if ($null -ne $capability -and $capability.State -ne 'Installed') {
                Add-WindowsCapability -Online -Name $capability.Name | Out-Null
            }
            Set-Service -Name sshd -StartupType Automatic
            Start-Service -Name sshd
            if (-not (Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue)) {
                New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null
            }
        } catch {
            if ("$_" -match '800f0954') { exit 3 }
            exit 2
        }
        """;

    /// <summary>The same steps for a person at a PowerShell of an administrator.</summary>
    private const string WINDOWS_INSTALL_MANUAL = """
        Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
        Set-Service -Name sshd -StartupType Automatic
        Start-Service sshd
        New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
        """;

    /// <summary>The PowerShell that lets the TLS port of the node through the Windows firewall; <c>{0}</c> is the port.</summary>
    private const string WINDOWS_OPEN_PORT = """
        $ErrorActionPreference = 'Stop'
        try {
            if (-not (Get-NetFirewallRule -Name 'RCynQ-Endpoint-{0}' -ErrorAction SilentlyContinue)) {
                New-NetFirewallRule -Name 'RCynQ-Endpoint-{0}' -DisplayName 'RCynQ ({0})' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort {0} | Out-Null
            }
        } catch {
            exit 2
        }
        """;

    /// <summary>The same step for a person at a PowerShell of an administrator; <c>{0}</c> is the port.</summary>
    private const string WINDOWS_OPEN_PORT_MANUAL =
        "New-NetFirewallRule -Name RCynQ-Endpoint-{0} -DisplayName 'RCynQ ({0})' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort {0}";

    /// <summary>The shell that lets a TCP port through an active ufw or firewalld; <c>{0}</c> is the port.</summary>
    private const string LINUX_FIREWALL = """
        if command -v ufw >/dev/null 2>&1 && ufw status | grep -q 'Status: active'; then ufw allow {0}/tcp; fi
        if command -v firewall-cmd >/dev/null 2>&1 && firewall-cmd --state >/dev/null 2>&1; then firewall-cmd --permanent --add-port={0}/tcp && firewall-cmd --reload; fi
        """;

    /// <summary>The shell that enables and starts the SSH server under either of its service names.</summary>
    private const string LINUX_START = "systemctl enable --now ssh 2>/dev/null || systemctl enable --now sshd";

    /// <summary>The shell that turns on Remote Login of macOS.</summary>
    private const string MAC_INSTALL = "systemsetup -setremotelogin on >/dev/null 2>&1 || launchctl load -w /System/Library/LaunchDaemons/ssh.plist";

    /// <summary>The package managers, the distributions they belong to and their command that installs the SSH server.</summary>
    private static readonly (string Tool, string[] Distributions, string Install)[] _packageManagers =
    {
        ("apt-get", new[] { "debian", "ubuntu" }, "apt-get update -q && DEBIAN_FRONTEND=noninteractive apt-get install -y openssh-server"),
        ("dnf", new[] { "fedora", "rhel", "centos" }, "dnf install -y openssh-server"),
        ("zypper", new[] { "suse", "opensuse" }, "zypper --non-interactive install openssh"),
        ("pacman", new[] { "arch" }, "pacman -S --noconfirm --needed openssh")
    };

    /// <summary>
    /// Plans the installation and start of the SSH server.
    /// </summary>
    /// <param name="system">The computer.</param>
    /// <returns>The plan.</returns>
    public static ServerSetupPlan PlanSshServer(HostSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        switch (system.Platform)
        {
            case HostPlatform.Windows:
                return new ServerSetupPlan(PowerShell(WINDOWS_INSTALL), WINDOWS_INSTALL_MANUAL);
            case HostPlatform.MacOs:
                return new ServerSetupPlan(MacShell(MAC_INSTALL), "sudo systemsetup -setremotelogin on");
            case HostPlatform.Linux:
                break;
            default:
                return ServerSetupPlan.Manual("ServerSetup.Unsupported", string.Empty);
        }

        var manager = _packageManagers.FirstOrDefault(candidate => system.Has(candidate.Tool) && candidate.Distributions.Any(system.IsLike));
        if (manager.Tool is null)
        {
            manager = _packageManagers.FirstOrDefault(candidate => system.Has(candidate.Tool));
        }

        if (manager.Tool is null)
        {
            return ServerSetupPlan.Manual("ServerSetup.NoPackageManager", "sudo apt install openssh-server\nsudo systemctl enable --now ssh");
        }

        var script = string.Join('\n', "set -e", manager.Install, LINUX_START, Firewall(SSH_PORT));
        return LinuxPlan(system, script);
    }

    /// <summary>
    /// Plans letting the TLS port of the node through the firewall.
    /// </summary>
    /// <param name="system">The computer.</param>
    /// <param name="port">The port, 1–65535.</param>
    /// <returns>The plan, or <see langword="null"/> when the system has no firewall to change.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The port is out of range.</exception>
    public static ServerSetupPlan? PlanOpenPort(HostSystem system, int port)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var text = port.ToString(CultureInfo.InvariantCulture);
        switch (system.Platform)
        {
            case HostPlatform.Windows:
                return new ServerSetupPlan(PowerShell(WINDOWS_OPEN_PORT.Replace("{0}", text, StringComparison.Ordinal)), WINDOWS_OPEN_PORT_MANUAL.Replace("{0}", text, StringComparison.Ordinal));
            case HostPlatform.Linux when system.Has("ufw") || system.Has("firewall-cmd"):
                return LinuxPlan(system, Firewall(port));
            default:
                // macOS filters programs rather than ports, and a Linux without ufw or firewalld blocks nothing by
                // default.
                return null;
        }
    }

    /// <summary>
    /// Builds a Linux plan: the script behind <c>pkexec</c>, or by hand with <c>sudo</c> when there is no <c>pkexec</c>
    /// or no <c>systemctl</c>.
    /// </summary>
    /// <param name="system">The computer.</param>
    /// <param name="script">The script.</param>
    /// <returns>The plan.</returns>
    private static ServerSetupPlan LinuxPlan(HostSystem system, string script)
    {
        // The raw strings above take the line breaks of this source file, which a checkout on Windows turns into CRLF;
        // /bin/sh reads "fi\r" as no keyword at all.
        script = script.ReplaceLineEndings("\n");
        var manual = $"sudo sh -c '{script.Replace("'", "'\\''", StringComparison.Ordinal).Replace("\n", "; ", StringComparison.Ordinal)}'";
        if (!system.Has("systemctl"))
        {
            return ServerSetupPlan.Manual("ServerSetup.Unsupported", manual);
        }

        return system.Has("pkexec")
            ? new ServerSetupPlan(new ElevatedCommand(ElevationKind.Polkit, "pkexec", new[] { "/bin/sh", "-c", script }), manual)
            : ServerSetupPlan.Manual("ServerSetup.NoPkexec", manual);
    }

    /// <summary>
    /// Builds the firewall part of a Linux script for a port.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <returns>The script.</returns>
    private static string Firewall(int port) => LINUX_FIREWALL.Replace("{0}", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// Builds a hidden PowerShell that runs a script passed as an encoded command, so no quoting can change it.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <returns>The command.</returns>
    private static ElevatedCommand PowerShell(string script)
        => new(
            ElevationKind.WindowsUac,
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) });

    /// <summary>
    /// Builds an <c>osascript</c> that runs a shell script with administrator privileges.
    /// </summary>
    /// <param name="script">The script; it must contain no double quote and no backslash.</param>
    /// <returns>The command.</returns>
    private static ElevatedCommand MacShell(string script)
    {
        if (script.Contains('"') || script.Contains('\\'))
        {
            throw new ArgumentException("The script must not contain quotes or backslashes.", nameof(script));
        }

        return new ElevatedCommand(ElevationKind.MacAdministrator, "osascript", new[] { "-e", $"do shell script \"{script}\" with administrator privileges" });
    }
}
