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
    /// Verifies that a valid Workstation configuration with a relay hop produces no errors and maps correctly.
    /// </summary>
    [Fact]
    public void Valid_Workstation_Options_Map_To_Domain()
    {
        var options = CreateWorkstationOptions();

        SyncBridgeOptionsValidator.Validate(options, TestTexts.English).Should().BeEmpty();

        var node = SyncBridgeOptionsValidator.ToNodeConfig(options);
        node.Role.Should().Be(NodeRole.Workstation);
        node.Name.Should().Be("workstation-1");

        var chain = SyncBridgeOptionsValidator.ToChainLinks(options);
        chain.Select(link => link.Name).Should().Equal("hop", "endpoint");
        chain[1].Id.Should().Be(SyncBridgeOptionsValidator.StableId("endpoint"));
    }

    /// <summary>
    /// Verifies that a link written without other routes has none, and that written routes include the address of the
    /// link itself, default to its user and port, and keep their identity between starts.
    /// </summary>
    [Fact]
    public void Routes_Of_A_Link_Are_Mapped_From_The_Configuration()
    {
        var options = CreateWorkstationOptions();
        SyncBridgeOptionsValidator.ToChainLinks(options).Should().OnlyContain(link => link.Routes.Count == 0);

        options.Chain[1].Routes.Add(new LinkRouteOptions { Host = "10.9.0.1", Priority = 1 });
        options.Chain[1].Routes.Add(new LinkRouteOptions { Name = "lte", Host = "10.10.0.1", SshPort = 2200, SshUser = "hop" });

        SyncBridgeOptionsValidator.Validate(options, TestTexts.English).Should().BeEmpty();
        var hop = SyncBridgeOptionsValidator.ToChainLinks(options)[0];
        hop.Routes.Select(route => (route.Name, route.Ssh.JumpSpec, route.Priority)).Should().Equal(
            ("lte", "hop@10.10.0.1:2200", 0),
            ("relay.example", "jump@relay.example:22", 0),
            ("10.9.0.1", "jump@10.9.0.1:22", 1));
        hop.ActiveRoute!.Ssh.Host.Should().Be("relay.example");
        SyncBridgeOptionsValidator.ToChainLinks(options)[0].Routes.Select(route => route.Id)
            .Should().Equal(hop.Routes.Select(route => route.Id), "a written route keeps its identity");

        options.Chain[1].Routes.Add(new LinkRouteOptions { Host = "relay.example" });
        SyncBridgeOptionsValidator.Validate(options, TestTexts.English).Should().ContainSingle("the same address twice is one route");
    }

    /// <summary>
    /// Verifies that all problems are reported together.
    /// </summary>
    [Fact]
    public void Invalid_Options_Report_All_Errors()
    {
        var options = CreateWorkstationOptions();
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
    /// Verifies the chain topology rules for endpoints and relays.
    /// </summary>
    [Fact]
    public void Chain_Topology_Is_Validated()
    {
        var options = CreateWorkstationOptions();
        // Chain[0] is the endpoint link, Chain[1] is the relay hop.
        options.Chain[0].PeerRole = NodeRole.Relay;
        options.Chain[1].PeerRole = NodeRole.Origin;

        var errors = SyncBridgeOptionsValidator.Validate(options, TestTexts.English);

        errors.Should().Contain(error => error.Contains("last chain link must point to the Origin"));
        errors.Should().Contain(error => error.Contains("must point to a Relay"));
    }

    /// <summary>
    /// Verifies that duplicate link identifiers are reported.
    /// </summary>
    [Fact]
    public void Duplicate_Link_Ids_Are_Reported()
    {
        var options = CreateWorkstationOptions();
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
        var options = CreateWorkstationOptions();
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
    /// Creates a valid Workstation configuration: Workstation → Relay → Origin.
    /// </summary>
    /// <returns>The options.</returns>
    internal static SyncBridgeOptions CreateWorkstationOptions() => new()
    {
        NodeId = "workstation-1",
        Role = NodeRole.Workstation,
        Repositories = { new RepositoryOptions { Name = "app", Path = "/work" } },
        Chain =
        {
            new ChainLinkOptions
            {
                Name = "endpoint", PeerNodeId = "origin-1", PeerRole = NodeRole.Origin, Host = "origin.internal",
                SshUser = "rcynq", RemotePort = 18443, Priority = 10
            },
            new ChainLinkOptions
            {
                Name = "hop", PeerNodeId = "relay-1", PeerRole = NodeRole.Relay, Host = "relay.example",
                SshUser = "jump", RemotePort = 22, Priority = 0, Transport = TransportKind.AmneziaWG,
                FallbackTransports = { TransportKind.Direct }
            }
        }
    };
}
