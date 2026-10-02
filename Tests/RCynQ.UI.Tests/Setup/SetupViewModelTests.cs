using System.Text.Json.Nodes;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dialogs;
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
                "nodeId": "old", "Role": "Workstation", "WorkdirPath": "/work",
                "Ssh": { "IdentityFile": "/keys/id" },
                "Chain": [
                  { "Name": "relay", "PeerNodeId": "relay-1", "PeerRole": "Relay", "Host": "203.0.113.4", "SshUser": "jump", "RemotePort": 22, "Priority": 10 },
                  { "Name": "laptop", "PeerNodeId": "workstation-1", "PeerRole": "Workstation", "Host": "10.8.0.3", "SshUser": "dev", "RemotePort": 18443, "Priority": 1000 }
                ]
              }
            }
            """);

        var errors = await LocalConfigFile.SaveAsync(path, new NodeSetupValues("origin-1", "Origin", NodeRole.Origin, "/srv/repo", 18500, "token-123"), TestTexts.Russian);

        errors.Should().BeEmpty();
        var section = JsonNode.Parse(await File.ReadAllTextAsync(path))!["SyncBridge"]!.AsObject();
        section.Select(pair => pair.Key).Should().Contain(new[] { "NodeId", "Role", "Repositories", "Ssh", "Chain" }).And.NotContain(new[] { "nodeId", "WorkdirPath", "RepoPath" });
        var options = LocalConfigFile.TryLoad(path)!;
        options.NodeId.Should().Be("origin-1");
        options.Role.Should().Be(NodeRole.Origin);
        options.Repositories.Should().ContainSingle().Which.Should().BeEquivalentTo(new Core.Config.RepositoryOptions { Name = "repo", Path = "/srv/repo" }, "the working copy the node had becomes the repository of the Origin, named after its folder");
        options.ListenPort.Should().Be(18500);
        options.LinkToken.Should().Be("token-123");
        options.Ssh.IdentityFile.Should().Be("/keys/id");
        options.Chain.Select(link => link.Name).Should().Equal("relay", "laptop");
        File.Exists(path + LocalConfigFile.UNREADABLE_BACKUP_SUFFIX).Should().BeFalse("a readable file needs no copy");
    }

    /// <summary>
    /// Verifies that a configuration file that cannot be read is kept as a copy when the setup starts the file anew: the
    /// links and settings in it, often behind one typing error, must not be gone for good.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Save_Keeps_A_Copy_Of_A_File_It_Could_Not_Read()
    {
        var path = Path.Combine(_root, "syncbridge.json");
        const string BROKEN = "{ \"SyncBridge\": { \"NodeId\": \"old\", \"Chain\": [ { \"Name\": \"relay\" } ] ";
        await File.WriteAllTextAsync(path, BROKEN);

        var errors = await LocalConfigFile.SaveAsync(path, new NodeSetupValues("origin-1", "Origin", NodeRole.Origin, "/srv/repo", 18500, "token-123"), TestTexts.Russian);

        errors.Should().BeEmpty();
        LocalConfigFile.TryLoad(path)!.NodeId.Should().Be("origin-1");
        (await File.ReadAllTextAsync(path + LocalConfigFile.UNREADABLE_BACKUP_SUFFIX)).Should().Be(BROKEN);
    }

    /// <summary>
    /// Verifies that the connection mode chosen in the setup reaches the configuration file and comes back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Endpoint_Mode_Is_Written_And_Loaded()
    {
        var path = Path.Combine(_root, "syncbridge.json");
        var setup = new SetupViewModel(TestTexts.Russian, path, Substitute.For<IDialogService>());
        setup.Load(null, null);

        setup.SelectedEndpointMode.Value.Should().Be(EndpointMode.SshTunnel, "the tunnel stays the default");
        setup.IsEndpointPortVisible.Should().BeFalse();

        setup.SelectedRole = NodeRole.Origin;
        setup.Path = "/srv/repo";
        setup.SelectedEndpointMode = setup.EndpointModes.Single(option => option.Value == EndpointMode.Direct);
        setup.IsEndpointPortVisible.Should().BeTrue();
        setup.EndpointPort = 18999;
        await setup.SaveCommand.ExecuteAsync(null);

        setup.HasError.Should().BeFalse(setup.Error);
        var written = LocalConfigFile.TryLoad(path)!;
        written.Endpoint.Mode.Should().Be(EndpointMode.Direct);
        written.Endpoint.Port.Should().Be(18999);
        written.Endpoint.BindAddress.Should().Be("0.0.0.0");

        var reopened = new SetupViewModel(TestTexts.Russian, path, Substitute.For<IDialogService>());
        reopened.Load(written, null);
        reopened.SelectedEndpointMode.Value.Should().Be(EndpointMode.Direct);
        reopened.EndpointPort.Should().Be(18999);

        // A Relay relays through its SSH server: the choice is hidden and never saved as Direct.
        reopened.SelectedRole = NodeRole.Relay;
        reopened.IsEndpointModeVisible.Should().BeFalse();
        reopened.IsEndpointPortVisible.Should().BeFalse();
        await reopened.SaveCommand.ExecuteAsync(null);
        reopened.HasError.Should().BeFalse(reopened.Error);
        LocalConfigFile.TryLoad(path)!.Endpoint.Mode.Should().Be(EndpointMode.SshTunnel);

        var relayDirect = await LocalConfigFile.SaveAsync(
            Path.Combine(_root, "relay.json"),
            new NodeSetupValues("relay-1", "M", NodeRole.Relay, string.Empty, 18443, string.Empty, EndpointMode.Direct, 18444), TestTexts.Russian);
        relayDirect.Should().Contain(error => error.Contains("SshTunnel"), "the configuration check refuses a Relay without SSH");
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
            new NodeSetupValues("origin-1", "Origin", NodeRole.Origin, "/srv/repo", 18443, "token", EndpointMode.Direct, 18443), TestTexts.Russian);

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

        var errors = await LocalConfigFile.SaveAsync(path, new NodeSetupValues("bad id", "x", NodeRole.Relay, string.Empty, 18443, string.Empty), TestTexts.Russian);

        errors.Should().NotBeEmpty();
        File.Exists(path).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the path is required for Workstation and that a Relay setup completes without one.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Save_Validates_Role_And_Completes()
    {
        var path = Path.Combine(_root, "nested", "syncbridge.json");
        var setup = new SetupViewModel(TestTexts.Russian, path, Substitute.For<IDialogService>());
        var completed = 0;
        setup.Completed += (_, _) => completed++;
        setup.Load(null, null);

        setup.SelectedRole = NodeRole.Workstation;
        await setup.SaveCommand.ExecuteAsync(null);
        setup.HasError.Should().BeTrue();
        completed.Should().Be(0);

        setup.SelectedRole = NodeRole.Relay;
        await setup.SaveCommand.ExecuteAsync(null);

        setup.HasError.Should().BeFalse();
        completed.Should().Be(1);
        LocalConfigFile.TryLoad(path)!.Role.Should().Be(NodeRole.Relay);
        setup.IsPathVisible.Should().BeFalse();
        setup.IsTokenVisible.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an existing configuration and the error that opened the setup are shown.
    /// </summary>
    [Fact]
    public void Load_Prefills_Existing_Configuration()
    {
        var setup = new SetupViewModel(TestTexts.Russian, Path.Combine(_root, "syncbridge.json"), Substitute.For<IDialogService>());

        setup.Load(new Core.Config.SyncBridgeOptions { NodeId = "origin-1", Role = NodeRole.Origin, Repositories = { new Core.Config.RepositoryOptions { Name = "repo", Path = "/srv/repo" } }, ListenPort = 18600, LinkToken = "t" }, "Invalid configuration");

        setup.NodeId.Should().Be("origin-1");
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
