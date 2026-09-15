using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Routing;
using Xunit;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests for SSH argument building and remote execution.
/// </summary>
public class SshCommandTests
{
    /// <summary>
    /// The first jump host.
    /// </summary>
    private static readonly SshEndpoint Relay1 = new("jump", "relay1.example", 22);

    /// <summary>
    /// The second jump host.
    /// </summary>
    private static readonly SshEndpoint Relay2 = new("jump", "10.8.0.2", 2222);

    /// <summary>
    /// The target endpoint.
    /// </summary>
    private static readonly SshEndpoint Dest = new("rcynq", "dest.internal", 22);

    /// <summary>
    /// Verifies the jump host argument.
    /// </summary>
    [Fact]
    public void ProxyJump_Builds_Comma_Separated_Chain()
    {
        ProxyJumpBuilder.Build(Array.Empty<SshEndpoint>()).Should().BeEmpty();
        ProxyJumpBuilder.Build(new[] { Relay1, Relay2 }).Should().Equal("-J", "jump@relay1.example:22,jump@10.8.0.2:2222");
    }

    /// <summary>
    /// Verifies the complete remote execution argument list.
    /// </summary>
    [Fact]
    public void BuildExec_Produces_Safe_Arguments()
    {
        var builder = new SshCommandBuilder(new SshClientOptions { ConnectTimeoutSeconds = 7, IdentityFile = "/home/u/.ssh/rcynq" });

        builder.BuildExec(Dest, new[] { Relay1 }, "git --version").Should().Equal(
            "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", "ConnectTimeout=7",
            "-i", "/home/u/.ssh/rcynq", "-o", "IdentitiesOnly=yes",
            "-T",
            "-J", "jump@relay1.example:22",
            "-p", "22", "--", "rcynq@dest.internal", "git --version");

        var multiline = () => builder.BuildExec(Dest, Array.Empty<SshEndpoint>(), "echo a\nrm -rf /");
        multiline.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies the local forward argument list binds only to loopback.
    /// </summary>
    [Fact]
    public void BuildLocalForward_Binds_Loopback()
    {
        var args = new SshCommandBuilder(new SshClientOptions()).BuildLocalForward(Dest, new[] { Relay1, Relay2 }, 40001, "127.0.0.1", 18443);

        args.Should().ContainInOrder("-N", "-n", "-T", "-o", "ExitOnForwardFailure=yes");
        args.Should().ContainInOrder("-L", "127.0.0.1:40001:127.0.0.1:18443");
        args.Should().ContainInOrder("-J", "jump@relay1.example:22,jump@10.8.0.2:2222");
        args.TakeLast(4).Should().Equal("-p", "22", "--", "rcynq@dest.internal");

        var badHost = () => new SshCommandBuilder(new SshClientOptions()).BuildLocalForward(Dest, Array.Empty<SshEndpoint>(), 1, "-oProxyCommand=x", 2);
        badHost.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies the GIT_SSH_COMMAND value and rejection of unquotable values.
    /// </summary>
    [Fact]
    public void BuildGitSshCommand_Quotes_And_Validates()
    {
        new SshCommandBuilder(new SshClientOptions { IdentityFile = "C:\\Users\\me\\.ssh\\id" }).BuildGitSshCommand(new[] { Relay1 })
            .Should().Be("'ssh' -o 'BatchMode=yes' -o 'StrictHostKeyChecking=accept-new' -o 'ConnectTimeout=10' -i 'C:/Users/me/.ssh/id' -o 'IdentitiesOnly=yes' -J jump@relay1.example:22");

        var injected = () => new SshCommandBuilder(new SshClientOptions { IdentityFile = "/tmp/x'; touch /tmp/pwned; '" }).BuildGitSshCommand(Array.Empty<SshEndpoint>());
        injected.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies chain route resolution by priority.
    /// </summary>
    [Fact]
    public void ChainRoute_Resolves_Jumps_And_Endpoint()
    {
        var mediator = new PeerDescriptor(new NodeId("m"), "m", NodeRole.Mediator, "relay1.example", 22, "jump");
        var dest = new PeerDescriptor(new NodeId("d"), "d", NodeRole.Dest, "dest.internal", 22, "rcynq");
        var chain = new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "endpoint", TransportKind.Direct, dest, 0, 18443, 5),
            new ChainLinkConfig(Guid.NewGuid(), "hop", TransportKind.AmneziaWG, mediator, 0, 22, 1)
        };

        var route = ChainRoute.Resolve(chain);

        route.Endpoint.Name.Should().Be("endpoint");
        route.Target.Should().Be(Dest);
        route.Jumps.Should().Equal(Relay1);
        var empty = () => ChainRoute.Resolve(Array.Empty<ChainLinkConfig>());
        empty.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies that remote execution returns remote exit codes and raises SSH failures.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SystemSshRunner_Maps_Exit_Codes()
    {
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(
            new ProcessResult(3, "out", "remote failure"),
            new ProcessResult(255, "", "Host key verification failed."));
        var runner = new SystemSshRunner(processes, new SshCommandBuilder(new SshClientOptions { SshExecutable = "/usr/bin/ssh" }));

        var result = await runner.ExecuteAsync(Dest, Array.Empty<SshEndpoint>(), "false");
        result.ExitCode.Should().Be(3);
        result.Succeeded.Should().BeFalse();

        var failing = () => runner.ExecuteAsync(Dest, Array.Empty<SshEndpoint>(), "true");
        (await failing.Should().ThrowAsync<SshExecutionException>()).Which.Message.Should().Contain("Host key verification failed");
        await processes.Received(2).RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "/usr/bin/ssh"), Arg.Any<CancellationToken>());
    }
}
