using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.Pages;

/// <summary>
/// Tests of <see cref="RoleAndNodesViewModel"/>.
/// </summary>
public sealed class RoleAndNodesViewModelTests
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Verifies the chain node rows and their statuses.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refresh_Shows_Nodes_With_Status()
    {
        var workstation = TestData.Link("workstation-1", "workstation-1", NodeRole.Workstation, "10.8.0.2");
        var relay = TestData.Link("relay", "relay-1", NodeRole.Relay, "203.0.113.4");
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Origin));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(new[] { workstation, relay });
        _api.GetLinkStatusAsync(Arg.Any<CancellationToken>()).Returns(new[] { TestData.State(workstation, LinkStatus.Ready) });
        var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs);

        await page.RefreshAsync();

        page.SelectedRole.Should().Be(NodeRole.Origin);
        page.PathLabel.Should().Be("Путь к репозиторию");
        page.Nodes.Select(node => (node.Name, node.Role, node.Address, node.StatusText)).Should().Equal(
            ("WORKSTATION-1", "Workstation", "10.8.0.2", "онлайн"),
            ("RELAY-1", "Relay", "203.0.113.4", "ожидание"));
        page.HasNoNodes.Should().BeFalse();
        page.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a role change is tracked and saved with the working path.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Role_Change_Is_Saved()
    {
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Relay));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkDto>());
        _api.SetRoleAsync(Arg.Any<SetRoleRequest>(), Arg.Any<CancellationToken>()).Returns(new RoleChangeResult(true, true, "restart"));
        var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs);
        await page.RefreshAsync();

        var restartRequested = false;
        page.RestartRequested += (_, _) => restartRequested = true;
        page.SelectedRole = NodeRole.Workstation;
        page.Path = "/work/app";
        page.IsDirty.Should().BeTrue();
        await page.ApplyRoleCommand.ExecuteAsync(null);

        await _api.Received(1).SetRoleAsync(new SetRoleRequest(NodeRole.Workstation, null, "/work/app"), Arg.Any<CancellationToken>());
        page.IsDirty.Should().BeFalse();
        page.InfoMessage.Should().Be("Роль сохранена, сервис перезапускается…");
        restartRequested.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that removal requires confirmation.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Remove_Requires_Confirmation()
    {
        var link = TestData.Link("relay", "relay-1", NodeRole.Relay, "203.0.113.4");
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Workstation));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(new[] { link });
        var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs);
        await page.RefreshAsync();

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        await page.RemoveNodeCommand.ExecuteAsync(page.Nodes.Single());
        await _api.DidNotReceiveWithAnyArgs().DeleteLinkAsync(default, default);

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        await page.RemoveNodeCommand.ExecuteAsync(page.Nodes.Single());
        await _api.Received(1).DeleteLinkAsync(link.Id, Arg.Any<CancellationToken>());
        page.HasNoNodes.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the shared token is shown hidden, can be revealed and copied, and that a Relay has none.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Link_Token_Is_Shown_And_Copied()
    {
        var settings = Substitute.For<ILocalNodeSettings>();
        settings.Read().Returns(new SyncBridgeOptions { NodeId = "origin-1", Role = NodeRole.Origin, Repositories = { new RepositoryOptions { Name = "repo", Path = "/srv/repo" } }, LinkToken = "secret-token" });
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Origin));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkDto>());
        _dialogs.CopyToClipboardAsync(Arg.Any<string>()).Returns(true);
        var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs, null, settings);

        await page.RefreshAsync();

        page.AreSettingsVisible.Should().BeTrue();
        page.NodeId.Should().Be("origin-1");
        page.LinkToken.Should().Be("secret-token");
        page.HasLinkToken.Should().BeTrue();
        page.IsTokenRevealed.Should().BeFalse("the token starts hidden");
        page.TokenMaskChar.Should().Be('•');
        page.AreSettingsDirty.Should().BeFalse();

        page.RevealTokenCommand.Execute(null);
        page.IsTokenRevealed.Should().BeTrue();
        page.TokenMaskChar.Should().Be('\0');
        page.RevealTokenText.Should().Be("Скрыть");

        await page.CopyTokenCommand.ExecuteAsync(null);
        await _dialogs.Received(1).CopyToClipboardAsync("secret-token");
        page.InfoMessage.Should().Contain("совпадать");

        page.SelectedRole = NodeRole.Relay;
        page.HasLinkToken.Should().BeFalse("a Relay has no shared token");
    }

    /// <summary>
    /// Verifies that a new token is created only after confirmation and that saving restarts the service.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Settings_Are_Saved_And_Service_Restarted()
    {
        var settings = Substitute.For<ILocalNodeSettings>();
        settings.Read().Returns(new SyncBridgeOptions { NodeId = "origin-1", Role = NodeRole.Origin, Repositories = { new RepositoryOptions { Name = "repo", Path = "/srv/repo" } }, ListenPort = 18443, LinkToken = "old-token" });
        settings.SaveAsync(Arg.Any<NodeSetupValues>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Origin));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkDto>());
        var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs, null, settings);
        await page.RefreshAsync();
        var restarts = 0;
        page.RestartRequested += (_, _) => restarts++;

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        await page.NewTokenCommand.ExecuteAsync(null);
        page.LinkToken.Should().Be("old-token", "a refused confirmation keeps the token");

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        await page.NewTokenCommand.ExecuteAsync(null);
        page.LinkToken.Should().NotBe("old-token").And.NotBeEmpty();
        page.AreSettingsDirty.Should().BeTrue();

        page.SelectedEndpointMode = page.EndpointModes.Single(option => option.Value == EndpointMode.Direct);
        page.EndpointPort = 18444;
        await page.SaveSettingsCommand.ExecuteAsync(null);

        await settings.Received(1).SaveAsync(
            Arg.Is<NodeSetupValues>(values => values.LinkToken == page.LinkToken &&
                values.EndpointMode == EndpointMode.Direct &&
                values.EndpointPort == 18444 &&
                values.NodeId == "origin-1" &&
                values.Role == NodeRole.Origin &&
                values.Path == "/srv/repo"),
            Arg.Any<CancellationToken>());
        page.AreSettingsDirty.Should().BeFalse();
        restarts.Should().Be(1);

        settings.SaveAsync(Arg.Any<NodeSetupValues>(), Arg.Any<CancellationToken>()).Returns(new[] { "Endpoint.Port 18443 …" });
        await page.SaveSettingsCommand.ExecuteAsync(null);
        page.ErrorMessage.Should().Contain("Endpoint.Port");
    }

    /// <summary>
    /// Verifies that the settings card stays hidden when the configuration file is unavailable.
    /// </summary>
    [Fact]
    public void Settings_Card_Needs_The_Config_File()
        => new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs).AreSettingsVisible.Should().BeFalse();

    /// <summary>
    /// Verifies that neighbour access asks for administrator rights only after confirmation.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Allow_Access_Elevates_After_Confirmation()
    {
        const string ADMIN_FILE = @"C:\ProgramData\ssh\administrators_authorized_keys";
        _api.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(
            new PeerAccessResult(false, true, ADMIN_FILE, 2, Array.Empty<string>(), new[] { "workstation-1" }, "elevation"));
        var elevation = Substitute.For<IPeerAccessElevation>();
        elevation.IsAvailable.Returns(true);
        elevation.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(new ElevatedRunResult(ElevatedRunOutcome.Completed, "Подключения соседних узлов разрешены."));
        var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs, elevation);

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        await page.AllowAccessCommand.ExecuteAsync(null);
        await elevation.DidNotReceiveWithAnyArgs().AllowPeerAccessAsync(default);
        page.InfoMessage.Should().Contain("не разрешены");

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.Contains(ADMIN_FILE))).Returns(true);
        await page.AllowAccessCommand.ExecuteAsync(null);
        await elevation.Received(1).AllowPeerAccessAsync(Arg.Any<CancellationToken>());
        page.InfoMessage.Should().StartWith("Подключения соседних узлов разрешены.").And.Contain("workstation-1");

        elevation.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(new ElevatedRunResult(ElevatedRunOutcome.Failed, "boom"));
        await page.AllowAccessCommand.ExecuteAsync(null);
        page.ErrorMessage.Should().Be("boom");
    }

    /// <summary>
    /// Verifies the messages when no elevation is needed or none is available.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Allow_Access_Reports_Result_Without_Elevation()
    {
        _api.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(
            new PeerAccessResult(true, false, "/home/dev/.ssh/authorized_keys", 1, new[] { "origin-1" }, Array.Empty<string>(), "ok"));
        var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs);

        await page.AllowAccessCommand.ExecuteAsync(null);

        page.InfoMessage.Should().StartWith("Подключения соседних узлов разрешены (1).").And.Contain("origin-1");
        await _dialogs.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!);

        _api.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(
            new PeerAccessResult(false, true, @"C:\keys", 1, Array.Empty<string>(), Array.Empty<string>(), "elevation"));
        await page.AllowAccessCommand.ExecuteAsync(null);
        page.InfoMessage.Should().Contain("rcynq ssh allow");
    }

    /// <summary>
    /// Verifies that an import that meets a node of the same role asks what the file is and is repeated with the
    /// answer, and that nothing is imported when the question is dismissed.
    /// </summary>
    /// <param name="confirmed">Whether the user answers the question.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Import_Of_A_Known_Role_Asks_For_A_Decision(bool confirmed)
    {
        var file = Path.Combine(Path.GetTempPath(), $"rcynq-import-{Guid.NewGuid():N}.json");
        var relayId = new NodeId("relay-1");
        var export = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION, relayId, "Relay", NodeRole.Relay, null,
            "10.9.0.4", "jump", 22, 18443, null, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(export, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        try
        {
            var linkId = Guid.NewGuid();
            var asked = new ImportResult(relayId, NodeRole.Relay, false, null, "decide")
            {
                Decision = new ImportDecisionRequest(
                    true,
                    "10.9.0.4",
                    new[] { ImportDecision.AlternativeRoute, ImportDecision.Replace },
                    new[] { new ImportCandidate(linkId, "relay", relayId, "Relay", NodeRole.Relay, "203.0.113.4") })
            };
            _dialogs.PickOpenFileAsync(Arg.Any<string>()).Returns(file);
            _dialogs.ShowImportDecisionDialogAsync(Arg.Any<ImportDecisionDialogViewModel>()).Returns(confirmed);
            _api.ImportConfigAsync(Arg.Any<ExportedNodeConfiguration>(), null, null, Arg.Any<CancellationToken>()).Returns(asked);
            _api.ImportConfigAsync(Arg.Any<ExportedNodeConfiguration>(), ImportDecision.AlternativeRoute, null, Arg.Any<CancellationToken>())
                .Returns(new ImportResult(relayId, NodeRole.Relay, false, linkId, "added"));
            _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkDto>());
            _api.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(
                new PeerAccessResult(false, false, "keys", 0, Array.Empty<string>(), Array.Empty<string>(), "none"));
            var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs);

            await page.ImportCommand.ExecuteAsync(null);

            await _dialogs.Received(1).ShowImportDecisionDialogAsync(Arg.Is<ImportDecisionDialogViewModel>(dialog =>
                dialog.Options.Count == 2 && dialog.Decision == ImportDecision.AlternativeRoute && dialog.LinkId == null));
            await _api.Received(confirmed ? 1 : 0).ImportConfigAsync(
                Arg.Any<ExportedNodeConfiguration>(), ImportDecision.AlternativeRoute, null, Arg.Any<CancellationToken>());
            (page.InfoMessage ?? string.Empty).Contains("RELAY-1", StringComparison.OrdinalIgnoreCase).Should().Be(confirmed);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Verifies that the routes of a node are shown under it with their state, that the address of the node is the one
    /// of the route in use, and that picking a route pins it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Routes_Of_A_Node_Are_Shown_And_Can_Be_Pinned()
    {
        var relay = TestData.Link("relay", "relay-1", NodeRole.Relay, "10.8.0.1");
        var udp = new LinkRouteDto(Guid.NewGuid(), "udp", "10.8.0.1", 22, "jump", 0, false, RouteReachability.Unreachable, null, DateTimeOffset.UtcNow);
        var tcp = new LinkRouteDto(Guid.NewGuid(), "tcp", "10.9.0.1", 22, "jump", 0, true, RouteReachability.Reachable, 41.6, DateTimeOffset.UtcNow);
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Workstation));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(new[] { relay });
        _api.GetLinkRoutesAsync(Arg.Any<CancellationToken>()).Returns(new[] { new LinkRoutesDto(relay.Id, null, new[] { udp, tcp }) });
        _api.SelectLinkRouteAsync(relay.Id, udp.Id, Arg.Any<CancellationToken>())
            .Returns(new LinkRoutesDto(relay.Id, udp.Id, new[] { udp with { IsActive = true }, tcp with { IsActive = false } }));
        var page = new RoleAndNodesViewModel(TestTexts.Russian, _api, _dialogs);

        await page.RefreshAsync();

        var node = page.Nodes.Single();
        node.HasRoutes.Should().BeTrue();
        node.IsAutoRoute.Should().BeTrue();
        node.Address.Should().Be("10.9.0.1", "the route in use is the address of the node");
        node.Routes.Select(route => (route.Address, route.IsActive, route.StatusText)).Should().Equal(
            ("10.8.0.1:22", false, "нет ответа"),
            ("10.9.0.1:22", true, "отвечает, 42 мс"));

        await page.SelectRouteCommand.ExecuteAsync(node.Routes[0]);

        node.IsAutoRoute.Should().BeFalse();
        node.Routes[0].IsPinned.Should().BeTrue();
        node.Address.Should().Be("10.8.0.1");

        page.ApplyRoutes(new LinkRoutesDto(relay.Id, null, Array.Empty<LinkRouteDto>()));
        node.HasRoutes.Should().BeFalse("a node left with one address shows no routes");
    }
}
