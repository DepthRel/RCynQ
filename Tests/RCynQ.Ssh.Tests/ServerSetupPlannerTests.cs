using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using RCynQ.Ssh.Enums;
using RCynQ.Ssh.Server;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests of <see cref="ServerSetupPlanner"/>: the commands for every system, and the commands by hand when the system
/// cannot be changed automatically.
/// </summary>
public sealed class ServerSetupPlannerTests
{
    /// <summary>
    /// Verifies that Windows gets a hidden PowerShell behind UAC whose encoded script installs, starts and opens sshd.
    /// </summary>
    [Fact]
    public void Windows_Installs_The_Capability_Behind_Uac()
    {
        var plan = ServerSetupPlanner.PlanSshServer(Computer(HostPlatform.Windows));

        plan.Command!.Elevation.Should().Be(ElevationKind.WindowsUac);
        plan.Command.FileName.Should().Be("powershell.exe");
        plan.Command.Arguments.Should().OnlyContain(argument => !argument.Contains(' ') && !argument.Contains('"'), "runas joins the arguments into one command line");
        var script = Decode(plan.Command.Arguments[^1]);
        script.Should().Contain("Add-WindowsCapability").And.Contain("Start-Service -Name sshd").And.Contain("-LocalPort 22").And.Contain("exit 3");
        plan.ManualCommands.Should().Contain("Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0");
    }

    /// <summary>
    /// Verifies that a Linux distribution gets its own package manager behind pkexec, and the firewall of port 22.
    /// </summary>
    /// <param name="distributions">The <c>ID</c> and <c>ID_LIKE</c> of the distribution.</param>
    /// <param name="tool">The package manager it has.</param>
    /// <param name="install">The expected install command.</param>
    [Theory]
    [InlineData("ubuntu debian", "apt-get", "apt-get install -y openssh-server")]
    [InlineData("rocky rhel centos fedora", "dnf", "dnf install -y openssh-server")]
    [InlineData("opensuse-leap suse opensuse", "zypper", "zypper --non-interactive install openssh")]
    [InlineData("manjaro arch", "pacman", "pacman -S --noconfirm --needed openssh")]
    public void Linux_Uses_The_Package_Manager_Of_The_Distribution(string distributions, string tool, string install)
    {
        var plan = ServerSetupPlanner.PlanSshServer(Computer(HostPlatform.Linux, distributions, tool, "systemctl", "pkexec"));

        plan.Command!.Elevation.Should().Be(ElevationKind.Polkit);
        plan.Command.FileName.Should().Be("pkexec");
        plan.Command.Arguments.Take(2).Should().Equal("/bin/sh", "-c");
        plan.Command.Arguments[2].Should().Contain(install).And.Contain("systemctl enable --now ssh").And.Contain("ufw allow 22/tcp");
        plan.Command.Arguments[2].Should().NotContain("\r", "/bin/sh does not read CRLF, whatever line breaks the source was checked out with");
        plan.ManualCommands.Should().StartWith("sudo sh -c '").And.NotContain("\r");
    }

    /// <summary>
    /// Verifies that the package manager of the distribution wins over another one that happens to be installed, and
    /// that an unknown distribution takes the first manager found.
    /// </summary>
    [Fact]
    public void Linux_Picks_A_Package_Manager_By_The_Distribution_First()
    {
        ServerSetupPlanner.PlanSshServer(Computer(HostPlatform.Linux, "fedora", "apt-get", "dnf", "systemctl", "pkexec"))
            .Command!.Arguments[2].Should().Contain("dnf install").And.NotContain("apt-get");
        ServerSetupPlanner.PlanSshServer(Computer(HostPlatform.Linux, "someos", "zypper", "systemctl", "pkexec"))
            .Command!.Arguments[2].Should().Contain("zypper");
    }

    /// <summary>
    /// Verifies the Linux cases without a way to do it automatically: no package manager, no pkexec, no systemctl.
    /// </summary>
    [Fact]
    public void Linux_Without_The_Tools_Gets_The_Commands_By_Hand()
    {
        var noManager = ServerSetupPlanner.PlanSshServer(Computer(HostPlatform.Linux, "debian", "systemctl", "pkexec"));
        var noPkexec = ServerSetupPlanner.PlanSshServer(Computer(HostPlatform.Linux, "debian", "apt-get", "systemctl"));
        var noSystemd = ServerSetupPlanner.PlanSshServer(Computer(HostPlatform.Linux, "debian", "apt-get", "pkexec"));

        noManager.Command.Should().BeNull();
        noManager.ReasonKey.Should().Be("ServerSetup.NoPackageManager");
        noPkexec.Command.Should().BeNull();
        noPkexec.ReasonKey.Should().Be("ServerSetup.NoPkexec");
        noPkexec.ManualCommands.Should().Contain("sudo sh -c '").And.Contain("apt-get install -y openssh-server");
        noSystemd.ReasonKey.Should().Be("ServerSetup.Unsupported");
    }

    /// <summary>
    /// Verifies that macOS turns on Remote Login through the administrator prompt of osascript.
    /// </summary>
    [Fact]
    public void MacOs_Turns_On_Remote_Login()
    {
        var plan = ServerSetupPlanner.PlanSshServer(Computer(HostPlatform.MacOs));

        plan.Command!.Elevation.Should().Be(ElevationKind.MacAdministrator);
        plan.Command.FileName.Should().Be("osascript");
        plan.Command.Arguments.Should().Equal("-e", "do shell script \"systemsetup -setremotelogin on >/dev/null 2>&1 || launchctl load -w /System/Library/LaunchDaemons/ssh.plist\" with administrator privileges");
    }

    /// <summary>
    /// Verifies the firewall rule of the TLS port on every system, and that a port out of range is refused.
    /// </summary>
    [Fact]
    public void Open_Port_Plans_A_Firewall_Rule_Where_There_Is_A_Firewall()
    {
        var windows = ServerSetupPlanner.PlanOpenPort(Computer(HostPlatform.Windows), 18444)!;
        Decode(windows.Command!.Arguments[^1]).Should().Contain("RCynQ-Endpoint-18444").And.Contain("-LocalPort 18444");
        windows.ManualCommands.Should().Contain("-LocalPort 18444");

        var linux = ServerSetupPlanner.PlanOpenPort(Computer(HostPlatform.Linux, "ubuntu", "ufw", "systemctl", "pkexec"), 18444)!;
        linux.Command!.Arguments[2].Should().Contain("ufw allow 18444/tcp").And.Contain("--add-port=18444/tcp");

        ServerSetupPlanner.PlanOpenPort(Computer(HostPlatform.Linux, "ubuntu", "systemctl", "pkexec"), 18444).Should().BeNull("no firewall is installed");
        ServerSetupPlanner.PlanOpenPort(Computer(HostPlatform.MacOs), 18444).Should().BeNull();

        var outOfRange = () => ServerSetupPlanner.PlanOpenPort(Computer(HostPlatform.Windows), 70000);
        outOfRange.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Verifies that <c>/etc/os-release</c> is read into the distribution and its parents, and that tools are found in
    /// the given folders.
    /// </summary>
    [Fact]
    public void Os_Release_And_Tools_Are_Read()
    {
        const string OS_RELEASE = "NAME=\"Ubuntu\"\n# comment\nID=ubuntu\nID_LIKE=\"debian\"\nVERSION_ID=\"24.04\"\n";

        SystemHostSystemSource.ParseOsRelease(OS_RELEASE).Should().Equal("ubuntu", "debian");
        SystemHostSystemSource.ParseOsRelease(string.Empty).Should().BeEmpty();

        var present = new HashSet<string> { Path.Combine("/usr/bin", "apt-get"), Path.Combine("/usr/sbin", "ufw") };
        SystemHostSystemSource.FindTools(new[] { "/usr/bin", "/usr/sbin" }, present.Contains).Should().BeEquivalentTo("apt-get", "ufw");
    }

    /// <summary>
    /// Verifies with the installed shells that every script is valid syntax, without running any of them: PowerShell
    /// parses the Windows scripts, <c>sh -n</c> reads the Linux ones. A shell that is not installed skips its part.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Scripts_Are_Valid_Syntax()
    {
        var windows = Computer(HostPlatform.Windows);
        var linux = Computer(HostPlatform.Linux, "debian", "apt-get", "ufw", "systemctl", "pkexec");
        var powerShell = new[] { ServerSetupPlanner.PlanSshServer(windows), ServerSetupPlanner.PlanOpenPort(windows, 18444)! }
            .Select(plan => Decode(plan.Command!.Arguments[^1]));
        var shell = new[] { ServerSetupPlanner.PlanSshServer(linux), ServerSetupPlanner.PlanOpenPort(linux, 18444)! }
            .Select(plan => plan.Command!.Arguments[2]);

        foreach (var script in powerShell)
        {
            var parse = await RunAsync("powershell.exe", script, "-NoProfile", "-NonInteractive", "-Command", "$null = [scriptblock]::Create([Console]::In.ReadToEnd())");
            parse?.ExitCode.Should().Be(0, parse.Value.Error);
        }

        foreach (var script in shell)
        {
            var parse = await RunAsync("sh", script, "-n");
            parse?.ExitCode.Should().Be(0, parse.Value.Error);
        }
    }

    /// <summary>
    /// Runs an installed program with a script on its standard input.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="input">The script.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The exit code and the error output, or <see langword="null"/> when the program is not installed.</returns>
    private static async Task<(int ExitCode, string Error)?> RunAsync(string fileName, string input, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Win32Exception)
        {
            return null;
        }

        using (process)
        {
            await process!.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
            var error = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await error);
        }
    }

    /// <summary>
    /// Builds the description of a computer.
    /// </summary>
    /// <param name="platform">The system.</param>
    /// <param name="distributions">The distribution and its parents, separated by spaces.</param>
    /// <param name="tools">The tools it has.</param>
    /// <returns>The description.</returns>
    private static HostSystem Computer(HostPlatform platform, string distributions = "", params string[] tools)
        => new(platform, distributions.Split(' ', StringSplitOptions.RemoveEmptyEntries), tools.ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// Decodes the script of a PowerShell <c>-EncodedCommand</c>.
    /// </summary>
    /// <param name="encoded">The Base64 of the UTF-16 script.</param>
    /// <returns>The script.</returns>
    private static string Decode(string encoded) => Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
}
