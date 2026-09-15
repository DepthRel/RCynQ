using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Ssh.Commands;
using Xunit;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests of the private <c>known_hosts</c> file of RCynQ connections.
/// </summary>
public sealed class KnownHostsOptionTests
{
    /// <summary>
    /// Verifies that the configured file is passed to every connection and to Git.
    /// </summary>
    [Fact]
    public void Known_Hosts_File_Is_Passed_To_Ssh_And_Git()
    {
        var builder = new SshCommandBuilder(new SshClientOptions { KnownHostsFile = @"C:\RCynQ\data\keys\known_hosts" });

        builder.BuildCommonOptions().Should().ContainInOrder("-o", "UserKnownHostsFile=C:/RCynQ/data/keys/known_hosts");
        builder.BuildGitSshCommand(Array.Empty<SshEndpoint>()).Should().Contain("'UserKnownHostsFile=C:/RCynQ/data/keys/known_hosts'");
    }

    /// <summary>
    /// Verifies that paths OpenSSH would split or that are not set leave the option out.
    /// </summary>
    /// <param name="path">The path.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Program Files\RCynQ\data\keys\known_hosts")]
    [InlineData("/tmp/a\"b")]
    public void Unusable_Paths_Are_Skipped(string? path)
    {
        SshCommandBuilder.KnownHostsOption(path).Should().BeNull();
        new SshCommandBuilder(new SshClientOptions { KnownHostsFile = path }).BuildCommonOptions()
            .Should().NotContain(option => option.StartsWith("UserKnownHostsFile", StringComparison.Ordinal));
    }
}
