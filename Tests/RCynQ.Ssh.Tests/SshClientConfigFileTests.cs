using System.Diagnostics;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Forwarding;
using RCynQ.Ssh.Network;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests of the SSH client configuration RCynQ writes for its own connections.
/// </summary>
public sealed class SshClientConfigFileTests : IDisposable
{
    /// <summary>The temporary directory; its name has a space, as the profile of a Windows user often has.</summary>
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq ssh config tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that the configuration applies to every host and carries what a jump host would otherwise not get: the
    /// non-interactive mode, the identity of the node and its <c>known_hosts</c> file, with paths in quotes.
    /// </summary>
    [Fact]
    public void Configuration_Carries_The_Settings_For_Every_Hop()
    {
        var identity = Path.Combine(_directory, "ssh", "rcynq_ed25519");
        var knownHosts = Path.Combine(_directory, "known_hosts");

        var text = SshClientConfigFile.Render(new SshClientOptions { ConnectTimeoutSeconds = 7, IdentityFile = identity, KnownHostsFile = knownHosts });

        var lines = text.Split('\n').Select(line => line.Trim()).ToArray();
        lines.Should().Contain("Host *");
        lines.Should().Contain(new[]
        {
            "BatchMode yes",
            "StrictHostKeyChecking accept-new",
            "UpdateHostKeys no",
            "ConnectTimeout 7",
            $"IdentityFile \"{Slashes(identity)}\"",
            "IdentitiesOnly yes",
            $"UserKnownHostsFile \"{Slashes(knownHosts)}\""
        });
    }

    /// <summary>
    /// Verifies that without an identity file the keys of the user stay in use, that a percent sign of a path is not read
    /// as a token, that a path in the home directory is left to the client, and that a relative path is written in full:
    /// <c>ssh</c> runs in the directory of the configuration file, where a relative path would point elsewhere.
    /// </summary>
    [Fact]
    public void Configuration_Leaves_Out_What_Is_Not_Set_And_Writes_Paths_In_Full()
    {
        var percent = Path.Combine(_directory, "100%", "known_hosts");

        var text = SshClientConfigFile.Render(new SshClientOptions { KnownHostsFile = percent });
        var relative = SshClientConfigFile.Render(new SshClientOptions { IdentityFile = "~/.ssh/id_rcynq", KnownHostsFile = Path.Combine("keys", "known_hosts") });

        text.Should().NotContain("IdentityFile").And.NotContain("IdentitiesOnly");
        text.Should().Contain($"UserKnownHostsFile \"{Slashes(percent).Replace("%", "%%", StringComparison.Ordinal)}\"");
        relative.Should().Contain("IdentityFile \"~/.ssh/id_rcynq\"");
        relative.Should().Contain($"UserKnownHostsFile \"{Slashes(Path.GetFullPath(Path.Combine("keys", "known_hosts")))}\"");
    }

    /// <summary>
    /// Verifies that the file is written once and rewritten when a setting changes, that <c>ssh</c> gets the bare name
    /// of the file together with the directory to run in, and that a builder without a configuration file passes none.
    /// </summary>
    [Fact]
    public void Configuration_File_Is_Written_And_Passed_By_Name()
    {
        var options = new SshClientOptions { ConfigFile = Path.Combine(_directory, "keys", "ssh_config"), KnownHostsFile = Path.Combine(_directory, "keys", "known_hosts") };
        var builder = new SshCommandBuilder(options, new SshClientConfigFile(TestTexts.English, options));

        var common = builder.BuildCommonOptions();

        common.Take(2).Should().Equal("-F", "ssh_config");
        builder.WorkingDirectory.Should().Be(Path.Combine(_directory, "keys"));
        var written = File.GetLastWriteTimeUtc(options.ConfigFile);
        File.ReadAllText(options.ConfigFile).Should().Be(SshClientConfigFile.Render(options));
        builder.BuildCommonOptions();
        File.GetLastWriteTimeUtc(options.ConfigFile).Should().Be(written, "an unchanged configuration is not written again");

        options.ConnectTimeoutSeconds = 42;
        builder.BuildCommonOptions();
        File.ReadAllText(options.ConfigFile).Should().Contain("ConnectTimeout 42");

        var jump = new SshEndpoint("jump", "203.0.113.4");
        builder.BuildLocalForward(new SshEndpoint("rcynq", "origin.internal"), new[] { jump }, 45000, "127.0.0.1", 18443).Take(2).Should().Equal("-F", "ssh_config");
        builder.BuildExec(new SshEndpoint("rcynq", "origin.internal"), new[] { jump }, "true").Take(2).Should().Equal("-F", "ssh_config");

        var plain = new SshCommandBuilder(new SshClientOptions());
        plain.BuildCommonOptions().Should().NotContain("-F");
        plain.WorkingDirectory.Should().BeNull();
        new SshClientConfigFile(TestTexts.English, new SshClientOptions()).Ensure().Should().BeNull();
    }

    /// <summary>
    /// Verifies that every <c>ssh</c> RCynQ starts itself, for a remote command and for a forward, runs in the directory
    /// of the configuration file: the arguments name the file without a directory.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Ssh_Is_Started_In_The_Directory_Of_The_Configuration_File()
    {
        var options = new SshClientOptions { ConfigFile = Path.Combine(_directory, "keys", "ssh_config") };
        var builder = new SshCommandBuilder(options, new SshClientConfigFile(TestTexts.English, options));
        var directory = Path.Combine(_directory, "keys");
        var target = new SshEndpoint("rcynq", "origin.internal");
        var process = Substitute.For<IRunningProcess>();
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(new ProcessResult(0, string.Empty, string.Empty));
        processes.Start(Arg.Any<ProcessSpec>()).Returns(process);
        var probe = Substitute.For<ITcpProbe>();
        probe.CanConnectAsync("127.0.0.1", 45000, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false, true);

        await new SystemSshRunner(TestTexts.English, processes, builder).ExecuteAsync(target, Array.Empty<SshEndpoint>(), "true");
        await new ProcessLocalForwardManager(TestTexts.English, processes, probe, Substitute.For<IPortAllocator>(), builder, options)
            .StartAsync(target, Array.Empty<SshEndpoint>(), 45000, "127.0.0.1", 18443);

        await processes.Received(1).RunAsync(Arg.Is<ProcessSpec>(spec => spec.WorkingDirectory == directory && spec.Arguments[1] == "ssh_config"), Arg.Any<CancellationToken>());
        processes.Received(1).Start(Arg.Is<ProcessSpec>(spec => spec.WorkingDirectory == directory && spec.Arguments[1] == "ssh_config"));
    }

    /// <summary>
    /// Verifies the command Git runs: Git starts <c>ssh</c> in the working tree, so the command changes to the directory
    /// of the configuration file first, and relative paths of the options are passed in full.
    /// </summary>
    [Fact]
    public void Git_Changes_To_The_Directory_Of_The_Configuration_File()
    {
        var options = new SshClientOptions { ConfigFile = Path.Combine(_directory, "keys", "ssh_config"), IdentityFile = Path.Combine("keys", "id") };
        var builder = new SshCommandBuilder(options, new SshClientConfigFile(TestTexts.English, options));

        var command = builder.BuildGitSshCommand(new[] { new SshEndpoint("jump", "203.0.113.4") });

        command.Should().StartWith($"cd '{Slashes(Path.Combine(_directory, "keys"))}' && 'ssh' -F 'ssh_config' ");
        command.Should().Contain($"-i '{Slashes(Path.GetFullPath(Path.Combine("keys", "id")))}'");
        command.Should().EndWith("-J jump@203.0.113.4:22");
        new SshCommandBuilder(new SshClientOptions()).BuildGitSshCommand(Array.Empty<SshEndpoint>()).Should().StartWith("'ssh' -o ");
    }

    /// <summary>
    /// Verifies that a file name a shell would read is refused instead of being handed on to a jump host.
    /// </summary>
    /// <param name="fileName">The name of the configuration file.</param>
    [Theory]
    [InlineData("ssh config")]
    [InlineData("-oProxyCommand=x")]
    [InlineData("cfg;x")]
    [InlineData("cfg'x")]
    public void Configuration_File_Name_A_Shell_Would_Read_Is_Refused(string fileName)
    {
        var options = new SshClientOptions { ConfigFile = Path.Combine(_directory, fileName) };

        var build = () => new SshCommandBuilder(options, new SshClientConfigFile(TestTexts.English, options)).BuildCommonOptions();

        build.Should().Throw<ArgumentException>();
        Directory.EnumerateFileSystemEntries(_directory).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies with the installed SSH client that it accepts the written file and reads the settings from it, also the
    /// paths with spaces. Skipped on a machine without <c>ssh</c>.
    /// </summary>
    [Fact]
    public void Installed_Ssh_Client_Reads_The_Configuration()
    {
        var options = new SshClientOptions
        {
            ConfigFile = Path.Combine(_directory, "ssh_config"),
            IdentityFile = Path.Combine(_directory, "id key"),
            KnownHostsFile = Path.Combine(_directory, "known hosts")
        };
        var builder = new SshCommandBuilder(options, new SshClientConfigFile(TestTexts.English, options));

        var effective = RunSsh(builder.WorkingDirectory!, "-G", "-F", "ssh_config", "relay.example");
        if (effective is null)
        {
            return;
        }

        var lines = effective.Value.Output.Split('\n').Select(line => line.Trim().ToLowerInvariant()).ToArray();
        lines.Should().Contain("batchmode yes");
        lines.Should().Contain("stricthostkeychecking accept-new");
        lines.Should().Contain("identitiesonly yes");
        lines.Should().Contain(line => line.StartsWith("userknownhostsfile ", StringComparison.Ordinal) && line.EndsWith("known hosts", StringComparison.Ordinal));
        lines.Should().Contain(line => line.StartsWith("identityfile ", StringComparison.Ordinal) && line.EndsWith("id key", StringComparison.Ordinal));
        lines.Should().Contain(line => line.StartsWith("updatehostkeys ", StringComparison.Ordinal) && (line.EndsWith(" no", StringComparison.Ordinal) || line.EndsWith(" false", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Verifies with the installed SSH client what the whole design rests on: the process that contacts a jump host
    /// reads the same configuration file, also when the file lies in a directory with a space. Nothing listens on the
    /// port of the jump host, so the connection ends there. Skipped on a machine without <c>ssh</c>.
    /// </summary>
    [Fact]
    public void Installed_Ssh_Client_Hands_The_Configuration_To_A_Jump_Host()
    {
        var options = new SshClientOptions { ConfigFile = Path.Combine(_directory, "ssh_config"), ConnectTimeoutSeconds = 2, KnownHostsFile = Path.Combine(_directory, "known_hosts") };
        var builder = new SshCommandBuilder(options, new SshClientConfigFile(TestTexts.English, options));
        var arguments = new[] { "-v" }
            .Concat(builder.BuildExec(new SshEndpoint("rcynq", "127.0.0.1"), new[] { new SshEndpoint("jump", "127.0.0.1", 1) }, "true"))
            .ToArray();

        var result = RunSsh(builder.WorkingDirectory!, arguments);
        if (result is null)
        {
            return;
        }

        var readings = result.Value.Error.Split('\n').Count(line => line.Contains("Reading configuration data ssh_config", StringComparison.Ordinal));
        readings.Should().BeGreaterThanOrEqualTo(2, $"the client and the process for the jump host both read the file:\n{result.Value.Error}");
        result.Value.Error.Should().NotContain("Can't open user config file");
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// Writes a path with forward slashes, the way the configuration file and the Git command carry it.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path with forward slashes.</returns>
    private static string Slashes(string path) => path.Replace('\\', '/');

    /// <summary>
    /// Runs the installed <c>ssh</c>.
    /// </summary>
    /// <param name="workingDirectory">The directory to run in.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The output, or <see langword="null"/> when <c>ssh</c> is not installed or does not know <c>-G</c>.</returns>
    private static (string Output, string Error)? RunSsh(string workingDirectory, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("ssh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
            }

            if (!Task.WaitAll(new Task[] { error, output }, 10_000))
            {
                return null;
            }

            // An ssh that rejects the file is a failure; an ssh too old to know an option says nothing.
            error.Result.Should().NotContain("Bad configuration option").And.NotContain("terminating");
            return error.Result.Contains("unknown option", StringComparison.OrdinalIgnoreCase) || error.Result.Contains("illegal option", StringComparison.OrdinalIgnoreCase)
                ? null
                : (output.Result, error.Result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
