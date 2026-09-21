using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Setup;

namespace RCynQ.UI.Tests.Setup;

/// <summary>
/// Tests of <see cref="SetupViewModel"/> and <see cref="LocalConfigFile"/>.
/// </summary>
public sealed class SetupViewModelTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-setup-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that the machine name is turned into a valid node identifier.
    /// </summary>
    /// <param name="machineName">The machine name.</param>
    /// <param name="expected">The identifier.</param>
    [Theory]
    [InlineData("DESKTOP-ABC12", "desktop-abc12")]
    [InlineData("My PC_01", "my-pc_01")]
    [InlineData("  ", "node")]
    [InlineData("Ноутбук", "node")]
    public void Default_Node_Id_Is_Valid(string machineName, string expected)
        => SetupViewModel.DefaultNodeId(machineName).Should().Be(expected);

    /// <summary>
    /// Verifies that saving keeps the chain and other settings and switches the role-specific path.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Save_Preserves_Other_Settings()
    {
        var path = Path.Combine(_root, "syncbridge.json");
        await File.WriteAllTextAsync(path, """
            {
              // existing node
              "SyncBridge": {
                "nodeId": "old", "Role": "Source", "WorkdirPath": "/work",
                "Ssh": { "IdentityFile": "/keys/id" },
                "Chain": [
                  { "Name": "relay", "PeerNodeId": "mediator-1", "PeerRole": "Mediator", "Host": "203.0.113.4", "SshUser": "jump", "RemotePort": 22, "Priority": 10 },
                  { "Name": "laptop", "PeerNodeId": "source-1", "PeerRole": "Source", "Host": "10.8.0.3", "SshUser": "dev", "RemotePort": 18443, "Priority": 1000 }
                ]
              }
            }
            """);

        var errors = await LocalConfigFile.SaveAsync(path, new NodeSetupValues("dest-1", "Dest", NodeRole.Dest, "/srv/repo", 18500, "token-123"));

        errors.Should().BeEmpty();
        var section = JsonNode.Parse(await File.ReadAllTextAsync(path))!["SyncBridge"]!.AsObject();
        section.Select(pair => pair.Key).Should().Contain(new[] { "NodeId", "Role", "RepoPath", "Ssh", "Chain" }).And.NotContain(new[] { "nodeId", "WorkdirPath" });
        var options = LocalConfigFile.TryLoad(path)!;
        options.NodeId.Should().Be("dest-1");
        options.Role.Should().Be(NodeRole.Dest);
        options.RepoPath.Should().Be("/srv/repo");
        options.ListenPort.Should().Be(18500);
        options.LinkToken.Should().Be("token-123");
        options.Ssh.IdentityFile.Should().Be("/keys/id");
        options.Chain.Select(link => link.Name).Should().Equal("relay", "laptop");
    }

    /// <summary>
    /// Verifies that the connection mode chosen in the setup reaches the configuration file and comes back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Endpoint_Mode_Is_Written_And_Loaded()
    {
        var path = Path.Combine(_root, "syncbridge.json");
        var setup = new SetupViewModel(path, Substitute.For<IDialogService>());
        setup.Load(null, null);

        setup.SelectedEndpointMode.Value.Should().Be(EndpointMode.SshTunnel, "the tunnel stays the default");
        setup.IsEndpointPortVisible.Should().BeFalse();

        setup.SelectedRole = NodeRole.Dest;
        setup.Path = "/srv/repo";
        setup.SelectedEndpointMode = EndpointModeOption.For(EndpointMode.Direct);
        setup.IsEndpointPortVisible.Should().BeTrue();
        setup.EndpointPort = 18999;
        await setup.SaveCommand.ExecuteAsync(null);

        setup.HasError.Should().BeFalse(setup.Error);
        var written = LocalConfigFile.TryLoad(path)!;
        written.Endpoint.Mode.Should().Be(EndpointMode.Direct);
        written.Endpoint.Port.Should().Be(18999);
        written.Endpoint.BindAddress.Should().Be("0.0.0.0");

        var reopened = new SetupViewModel(path, Substitute.For<IDialogService>());
        reopened.Load(written, null);
        reopened.SelectedEndpointMode.Value.Should().Be(EndpointMode.Direct);
        reopened.EndpointPort.Should().Be(18999);

        // A Mediator relays through its SSH server: the choice is hidden and never saved as Direct.
        reopened.SelectedRole = NodeRole.Mediator;
        reopened.IsEndpointModeVisible.Should().BeFalse();
        reopened.IsEndpointPortVisible.Should().BeFalse();
        await reopened.SaveCommand.ExecuteAsync(null);
        reopened.HasError.Should().BeFalse(reopened.Error);
        LocalConfigFile.TryLoad(path)!.Endpoint.Mode.Should().Be(EndpointMode.SshTunnel);

        var mediatorDirect = await LocalConfigFile.SaveAsync(
            Path.Combine(_root, "mediator.json"),
            new NodeSetupValues("mediator-1", "M", NodeRole.Mediator, string.Empty, 18443, string.Empty, EndpointMode.Direct, 18444));
        mediatorDirect.Should().Contain(error => error.Contains("SshTunnel"), "the configuration check refuses a Mediator without SSH");
    }

    /// <summary>
    /// Verifies that a direct endpoint port colliding with the service port is refused.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Endpoint_Port_Must_Differ_From_Service_Port()
    {
        var path = Path.Combine(_root, "clash.json");

        var errors = await LocalConfigFile.SaveAsync(
            path,
            new NodeSetupValues("dest-1", "Dest", NodeRole.Dest, "/srv/repo", 18443, "token", EndpointMode.Direct, 18443));

        errors.Should().ContainSingle().Which.Should().Contain("Endpoint.Port");
        File.Exists(path).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an invalid configuration is not written.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Invalid_Values_Are_Not_Written()
    {
        var path = Path.Combine(_root, "syncbridge.json");

        var errors = await LocalConfigFile.SaveAsync(path, new NodeSetupValues("bad id", "x", NodeRole.Mediator, string.Empty, 18443, string.Empty));

        errors.Should().NotBeEmpty();
        File.Exists(path).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the path is required for Source and that a Mediator setup completes without one.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Save_Validates_Role_And_Completes()
    {
        var path = Path.Combine(_root, "nested", "syncbridge.json");
        var setup = new SetupViewModel(path, Substitute.For<IDialogService>());
        var completed = 0;
        setup.Completed += (_, _) => completed++;
        setup.Load(null, null);

        setup.SelectedRole = NodeRole.Source;
        await setup.SaveCommand.ExecuteAsync(null);
        setup.HasError.Should().BeTrue();
        completed.Should().Be(0);

        setup.SelectedRole = NodeRole.Mediator;
        await setup.SaveCommand.ExecuteAsync(null);

        setup.HasError.Should().BeFalse();
        completed.Should().Be(1);
        LocalConfigFile.TryLoad(path)!.Role.Should().Be(NodeRole.Mediator);
        setup.IsPathVisible.Should().BeFalse();
        setup.IsTokenVisible.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an existing configuration and the error that opened the setup are shown.
    /// </summary>
    [Fact]
    public void Load_Prefills_Existing_Configuration()
    {
        var setup = new SetupViewModel(Path.Combine(_root, "syncbridge.json"), Substitute.For<IDialogService>());

        setup.Load(new Core.Config.SyncBridgeOptions { NodeId = "dest-1", Role = NodeRole.Dest, RepoPath = "/srv/repo", ListenPort = 18600, LinkToken = "t" }, "Invalid configuration");

        setup.NodeId.Should().Be("dest-1");
        setup.Path.Should().Be("/srv/repo");
        setup.PathLabel.Should().Be("Путь к репозиторию");
        setup.ListenPort.Should().Be(18600);
        setup.Error.Should().Be("Invalid configuration");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
