using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Tests;

/// <summary>
/// Tests for <see cref="SyncBridgeOptionsValidator"/>.
/// </summary>
public class SyncBridgeOptionsValidatorTests
{
    /// <summary>
    /// Verifies that a valid Source configuration with a mediator hop produces no errors and maps correctly.
    /// </summary>
    [Fact]
    public void Valid_Source_Options_Map_To_Domain()
    {
        var options = CreateSourceOptions();

        SyncBridgeOptionsValidator.Validate(options, TestTexts.English).Should().BeEmpty();

        var node = SyncBridgeOptionsValidator.ToNodeConfig(options);
        node.Role.Should().Be(NodeRole.Source);
        node.Name.Should().Be("source-1");

        var chain = SyncBridgeOptionsValidator.ToChainLinks(options);
        chain.Select(link => link.Name).Should().Equal("hop", "endpoint");
        chain[1].Id.Should().Be(SyncBridgeOptionsValidator.StableId("endpoint"));
    }

    /// <summary>
    /// Verifies that all problems are reported together.
    /// </summary>
    [Fact]
    public void Invalid_Options_Report_All_Errors()
    {
        var options = CreateSourceOptions();
        options.NodeId = "bad id";
        options.ListenPort = 70000;
        options.Chain[0].Host = "-oProxyCommand=x";

        var errors = SyncBridgeOptionsValidator.Validate(options, TestTexts.English);

        errors.Should().HaveCount(3);
        errors.Should().Contain(error => error.StartsWith("Node:"));
        errors.Should().Contain(error => error.Contains("ListenPort"));
        errors.Should().Contain(error => error.StartsWith("Chain[0]:"));
    }

    /// <summary>
    /// Verifies the chain topology rules for endpoints and mediators.
    /// </summary>
    [Fact]
    public void Chain_Topology_Is_Validated()
    {
        var options = CreateSourceOptions();
        // Chain[0] is the endpoint link, Chain[1] is the mediator hop.
        options.Chain[0].PeerRole = NodeRole.Mediator;
        options.Chain[1].PeerRole = NodeRole.Dest;

        var errors = SyncBridgeOptionsValidator.Validate(options, TestTexts.English);

        errors.Should().Contain(error => error.Contains("last chain link must point to a Dest"));
        errors.Should().Contain(error => error.Contains("must point to a Mediator"));
    }

    /// <summary>
    /// Verifies that duplicate link identifiers are reported.
    /// </summary>
    [Fact]
    public void Duplicate_Link_Ids_Are_Reported()
    {
        var options = CreateSourceOptions();
        options.Chain[0].Name = "hop";

        SyncBridgeOptionsValidator.Validate(options, TestTexts.English).Should().Contain(error => error.Contains("duplicate link identifier"));
    }

    /// <summary>
    /// Verifies the name of the SSH configuration file: the directory may be any, also one with spaces, but the name is
    /// handed on to a shell unquoted and therefore may hold nothing a shell reads.
    /// </summary>
    /// <param name="path">The configured path.</param>
    /// <param name="valid">Whether the path is accepted.</param>
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("keys/ssh_config", true)]
    [InlineData("/data/John Smith/keys/ssh_config", true)]
    [InlineData("keys/rcynq-ssh.conf", true)]
    [InlineData("keys/ssh config", false)]
    [InlineData("keys/-F", false)]
    [InlineData("keys/cfg;rm", false)]
    [InlineData("keys/cfg$(x)", false)]
    [InlineData("keys/..", false)]
    [InlineData("keys/", false)]
    public void Ssh_Config_File_Name_Is_Restricted(string? path, bool valid)
    {
        var options = CreateSourceOptions();
        options.Ssh.ConfigFile = path;

        var errors = SyncBridgeOptionsValidator.Validate(options, TestTexts.English);

        if (valid)
        {
            errors.Should().BeEmpty();
        }
        else
        {
            errors.Should().ContainSingle().Which.Should().Contain("Ssh.ConfigFile").And.Contain(path);
        }
    }

    /// <summary>
    /// Creates a valid Source configuration: Source → Mediator → Dest.
    /// </summary>
    /// <returns>The options.</returns>
    internal static SyncBridgeOptions CreateSourceOptions() => new()
    {
        NodeId = "source-1",
        Role = NodeRole.Source,
        WorkdirPath = "/work",
        Chain =
        {
            new ChainLinkOptions
            {
                Name = "endpoint", PeerNodeId = "dest-1", PeerRole = NodeRole.Dest, Host = "dest.internal",
                SshUser = "rcynq", RemotePort = 18443, Priority = 10
            },
            new ChainLinkOptions
            {
                Name = "hop", PeerNodeId = "mediator-1", PeerRole = NodeRole.Mediator, Host = "relay.example",
                SshUser = "jump", RemotePort = 22, Priority = 0, Transport = TransportKind.AmneziaWG,
                FallbackTransports = { TransportKind.Direct }
            }
        }
    };
}
