using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Pages;
using Xunit;

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
        var source = TestData.Link("source-1", "source-1", NodeRole.Source, "10.8.0.2");
        var mediator = TestData.Link("relay", "mediator-1", NodeRole.Mediator, "203.0.113.4");
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Dest));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(new[] { source, mediator });
        _api.GetLinkStatusAsync(Arg.Any<CancellationToken>()).Returns(new[] { TestData.State(source, LinkStatus.Ready) });
        var page = new RoleAndNodesViewModel(_api, _dialogs);

        await page.RefreshAsync();

        page.SelectedRole.Should().Be(NodeRole.Dest);
        page.PathLabel.Should().Be("Путь к репозиторию");
        page.Nodes.Select(node => (node.Name, node.Role, node.Address, node.StatusText)).Should().Equal(
            ("SOURCE-1", "Source", "10.8.0.2", "онлайн"),
            ("MEDIATOR-1", "Mediator", "203.0.113.4", "ожидание"));
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
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Mediator));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkDto>());
        _api.SetRoleAsync(Arg.Any<SetRoleRequest>(), Arg.Any<CancellationToken>()).Returns(new RoleChangeResult(true, true, "restart"));
        var page = new RoleAndNodesViewModel(_api, _dialogs);
        await page.RefreshAsync();

        var restartRequested = false;
        page.RestartRequested += (_, _) => restartRequested = true;
        page.SelectedRole = NodeRole.Source;
        page.Path = "/work/app";
        page.IsDirty.Should().BeTrue();
        await page.ApplyRoleCommand.ExecuteAsync(null);

        await _api.Received(1).SetRoleAsync(new SetRoleRequest(NodeRole.Source, null, "/work/app"), Arg.Any<CancellationToken>());
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
        var link = TestData.Link("relay", "mediator-1", NodeRole.Mediator, "203.0.113.4");
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Source));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(new[] { link });
        var page = new RoleAndNodesViewModel(_api, _dialogs);
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
    /// Verifies that the shared token is shown hidden, can be revealed and copied, and that a Mediator has none.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Link_Token_Is_Shown_And_Copied()
    {
        var settings = Substitute.For<ILocalNodeSettings>();
        settings.Read().Returns(new SyncBridgeOptions { NodeId = "dest-1", Role = NodeRole.Dest, RepoPath = "/srv/repo", LinkToken = "secret-token" });
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Dest));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkDto>());
        _dialogs.CopyToClipboardAsync(Arg.Any<string>()).Returns(true);
        var page = new RoleAndNodesViewModel(_api, _dialogs, null, settings);

        await page.RefreshAsync();

        page.AreSettingsVisible.Should().BeTrue();
        page.NodeId.Should().Be("dest-1");
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

        page.SelectedRole = NodeRole.Mediator;
        page.HasLinkToken.Should().BeFalse("a Mediator has no shared token");
    }

    /// <summary>
    /// Verifies that a new token is created only after confirmation and that saving restarts the service.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Settings_Are_Saved_And_Service_Restarted()
    {
        var settings = Substitute.For<ILocalNodeSettings>();
        settings.Read().Returns(new SyncBridgeOptions { NodeId = "dest-1", Role = NodeRole.Dest, RepoPath = "/srv/repo", ListenPort = 18443, LinkToken = "old-token" });
        settings.SaveAsync(Arg.Any<NodeSetupValues>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Dest));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkDto>());
        var page = new RoleAndNodesViewModel(_api, _dialogs, null, settings);
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

        page.SelectedEndpointMode = EndpointModeOption.For(EndpointMode.Direct);
        page.EndpointPort = 18444;
        await page.SaveSettingsCommand.ExecuteAsync(null);

        await settings.Received(1).SaveAsync(
            Arg.Is<NodeSetupValues>(values => values.LinkToken == page.LinkToken
                && values.EndpointMode == EndpointMode.Direct
                && values.EndpointPort == 18444
                && values.NodeId == "dest-1"
                && values.Role == NodeRole.Dest
                && values.Path == "/srv/repo"),
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
        => new RoleAndNodesViewModel(_api, _dialogs).AreSettingsVisible.Should().BeFalse();

    /// <summary>
    /// Verifies that neighbour access asks for administrator rights only after confirmation.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Allow_Access_Elevates_After_Confirmation()
    {
        const string AdminFile = @"C:\ProgramData\ssh\administrators_authorized_keys";
        _api.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(
            new PeerAccessResult(false, true, AdminFile, 2, Array.Empty<string>(), new[] { "source-1" }, "elevation"));
        var elevation = Substitute.For<IPeerAccessElevation>();
        elevation.IsAvailable.Returns(true);
        elevation.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(new ElevatedRunResult(ElevatedRunOutcome.Completed, "Подключения соседних узлов разрешены."));
        var page = new RoleAndNodesViewModel(_api, _dialogs, elevation);

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        await page.AllowAccessCommand.ExecuteAsync(null);
        await elevation.DidNotReceiveWithAnyArgs().AllowPeerAccessAsync(default);
        page.InfoMessage.Should().Contain("не разрешены");

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.Contains(AdminFile))).Returns(true);
        await page.AllowAccessCommand.ExecuteAsync(null);
        await elevation.Received(1).AllowPeerAccessAsync(Arg.Any<CancellationToken>());
        page.InfoMessage.Should().StartWith("Подключения соседних узлов разрешены.").And.Contain("source-1");

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
            new PeerAccessResult(true, false, "/home/dev/.ssh/authorized_keys", 1, new[] { "dest-1" }, Array.Empty<string>(), "ok"));
        var page = new RoleAndNodesViewModel(_api, _dialogs);

        await page.AllowAccessCommand.ExecuteAsync(null);

        page.InfoMessage.Should().StartWith("Подключения соседних узлов разрешены (1).").And.Contain("dest-1");
        await _dialogs.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!);

        _api.AllowPeerAccessAsync(Arg.Any<CancellationToken>()).Returns(
            new PeerAccessResult(false, true, @"C:\keys", 1, Array.Empty<string>(), Array.Empty<string>(), "elevation"));
        await page.AllowAccessCommand.ExecuteAsync(null);
        page.InfoMessage.Should().Contain("rcynq ssh allow");
    }
}
