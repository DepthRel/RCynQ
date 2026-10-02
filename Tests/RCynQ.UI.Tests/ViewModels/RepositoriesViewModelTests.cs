using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Repositories;

namespace RCynQ.UI.Tests.ViewModels;

/// <summary>
/// Tests of the repository switcher: switching, downloading a repository a Workstation does not keep yet, and the notice
/// shown when the node at the other end switched.
/// </summary>
public sealed class RepositoriesViewModelTests
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>The folder of the working copy the Workstation has.</summary>
    private static readonly string _appFolder = Path.Combine(Path.GetTempPath(), "rcynq-repos", "app");

    /// <summary>
    /// Verifies that the list shows what the node keeps and what the Origin offers, with the current repository selected
    /// and without a switch being made by the loading itself.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Loading_Selects_The_Current_Repository_Without_Switching()
    {
        _api.GetRepositoriesAsync(true, Arg.Any<CancellationToken>()).Returns(Workstation("app"));
        var repositories = Create(NodeRole.Workstation);

        await repositories.LoadAsync();

        repositories.Items.Select(item => item.Name).Should().Equal("app", "tools");
        repositories.Selected!.Name.Should().Be("app");
        repositories.ActiveName.Should().Be("app");
        repositories.Items[1].IsLocal.Should().BeFalse();
        repositories.Items[1].CanSwitch.Should().BeTrue();
        repositories.Items[0].CanRemove.Should().BeFalse("the current repository cannot be removed");
        repositories.IsMismatch.Should().BeFalse();
        await _api.DidNotReceiveWithAnyArgs().ActivateRepositoryAsync(default!);
    }

    /// <summary>
    /// Verifies that a Relay shows no repositories and asks the service nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Relay_Has_No_Repositories()
    {
        var repositories = Create(NodeRole.Relay);

        await repositories.LoadAsync();

        repositories.IsAvailable.Should().BeFalse();
        _api.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that picking a repository the Workstation does not keep yet offers a folder next to the current working
    /// copy, adds the repository there, switches to it and reports that it is still to be pulled.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Picking_A_Repository_That_Is_Not_Downloaded_Asks_For_A_Folder_And_Switches()
    {
        var suggested = Path.Combine(Path.GetDirectoryName(_appFolder)!, "tools");
        _api.GetRepositoriesAsync(true, Arg.Any<CancellationToken>()).Returns(Workstation("app"));
        _api.AddRepositoryAsync("tools", suggested, Arg.Any<CancellationToken>()).Returns(Workstation("app", toolsFolder: suggested, offered: false));
        _api.ActivateRepositoryAsync("tools", Arg.Any<CancellationToken>()).Returns(Workstation("tools", toolsFolder: suggested, offered: false));
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.Contains(suggested))).Returns(true);
        var repositories = Create(NodeRole.Workstation);
        await repositories.LoadAsync();
        var switched = new List<bool>();
        repositories.Switched += (_, added) => switched.Add(added);

        repositories.Selected = repositories.Items[1];
        await Eventually(() => switched.Count == 1);

        switched.Should().Equal(true);
        repositories.ActiveName.Should().Be("tools");
        repositories.Selected!.Name.Should().Be("tools");
        await _dialogs.DidNotReceiveWithAnyArgs().PickFolderAsync(default!);
        Received.InOrder(() =>
        {
            _api.AddRepositoryAsync("tools", suggested, Arg.Any<CancellationToken>());
            _api.ActivateRepositoryAsync("tools", Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// Verifies that declining the suggested folder opens the folder picker, and that giving no folder switches nothing
    /// and puts the selection back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Declining_Every_Folder_Switches_Nothing()
    {
        _api.GetRepositoriesAsync(true, Arg.Any<CancellationToken>()).Returns(Workstation("app"));
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns((string?)null);
        var repositories = Create(NodeRole.Workstation);
        await repositories.LoadAsync();

        await repositories.SwitchCommand.ExecuteAsync(repositories.Items[1]);

        await _dialogs.Received(1).PickFolderAsync(Arg.Any<string>());
        repositories.Selected!.Name.Should().Be("app");
        await _api.DidNotReceiveWithAnyArgs().AddRepositoryAsync(default!, default!);
        await _api.DidNotReceiveWithAnyArgs().ActivateRepositoryAsync(default!);
    }

    /// <summary>
    /// Verifies that a refused switch shows the reason and keeps the node on its repository.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refused_Switch_Shows_The_Reason()
    {
        _api.GetRepositoriesAsync(true, Arg.Any<CancellationToken>()).Returns(Workstation("app", toolsFolder: "/work/tools"));
        _api.ActivateRepositoryAsync("tools", Arg.Any<CancellationToken>())
            .Returns<Task<RepositoryListDto>>(_ => throw new ServiceApiException(System.Net.HttpStatusCode.BadRequest, "refused by the node"));
        var repositories = Create(NodeRole.Workstation);
        await repositories.LoadAsync();
        var switched = false;
        repositories.Switched += (_, _) => switched = true;

        await repositories.SwitchCommand.ExecuteAsync(repositories.Items[1]);

        repositories.ErrorMessage.Should().Contain("refused by the node");
        repositories.Selected!.Name.Should().Be("app");
        switched.Should().BeFalse();
        repositories.IsBusy.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a switch of the other node is offered, never followed: the user either switches too or stays, and
    /// staying hides the notice until the other node switches again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Switch_Of_The_Other_Node_Is_Offered_Not_Followed()
    {
        _api.GetRepositoriesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Workstation("app", toolsFolder: "/work/tools"));
        _api.ActivateRepositoryAsync("tools", Arg.Any<CancellationToken>()).Returns(Workstation("tools", toolsFolder: "/work/tools"));
        var repositories = Create(NodeRole.Workstation);
        await repositories.LoadAsync();

        repositories.ApplyEvent(new RepositoryEvent("app", "tools", DateTimeOffset.UtcNow));

        repositories.IsMismatch.Should().BeTrue();
        repositories.MismatchText.Should().Contain("tools").And.Contain("app");
        repositories.ActiveName.Should().Be("app");
        await _api.DidNotReceiveWithAnyArgs().ActivateRepositoryAsync(default!);

        repositories.StayCommand.Execute(null);
        repositories.IsMismatch.Should().BeFalse();
        repositories.ApplyEvent(new RepositoryEvent("app", "tools", DateTimeOffset.UtcNow));
        repositories.IsMismatch.Should().BeFalse("the user chose to stay away from this repository");

        repositories.ApplyEvent(new RepositoryEvent("app", "docs", DateTimeOffset.UtcNow));
        repositories.IsMismatch.Should().BeTrue("another switch is news again");

        repositories.ApplyEvent(new RepositoryEvent("app", "tools", DateTimeOffset.UtcNow));
        await repositories.FollowPeerCommand.ExecuteAsync(null);

        repositories.ActiveName.Should().Be("tools");
        repositories.IsMismatch.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an Origin registers a repository by its folder, named after the folder, and that a taken name gets a
    /// number.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Origin_Registers_A_Repository_Named_After_Its_Folder()
    {
        var list = new RepositoryListDto(NodeRole.Origin, "tools", null, new[] { new RepositoryDto("tools", "/srv/tools", true, true, true) }, null);
        _api.GetRepositoriesAsync(true, Arg.Any<CancellationToken>()).Returns(list);
        _api.AddRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(list);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("/other/tools", "/srv/My Docs");
        var repositories = Create(NodeRole.Origin);
        await repositories.LoadAsync();

        await repositories.RegisterCommand.ExecuteAsync(null);
        await repositories.RegisterCommand.ExecuteAsync(null);

        repositories.CanRegister.Should().BeTrue();
        await _api.Received(1).AddRepositoryAsync("tools-2", "/other/tools", Arg.Any<CancellationToken>());
        await _api.Received(1).AddRepositoryAsync("My-Docs", "/srv/My Docs", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that removing asks first and says that the files stay.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Removing_Asks_First()
    {
        _api.GetRepositoriesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Workstation("app", toolsFolder: "/work/tools"));
        _api.RemoveRepositoryAsync("tools", Arg.Any<CancellationToken>()).Returns(Workstation("app", offered: false, withTools: false));
        var repositories = Create(NodeRole.Workstation);
        await repositories.LoadAsync();
        var tools = repositories.Items[1];

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        await repositories.RemoveCommand.ExecuteAsync(tools);
        await _api.DidNotReceiveWithAnyArgs().RemoveRepositoryAsync(default!);

        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.Contains("/work/tools"))).Returns(true);
        await repositories.RemoveCommand.ExecuteAsync(tools);
        await _api.Received(1).RemoveRepositoryAsync("tools", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creates the view model for a node of a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The view model.</returns>
    private RepositoriesViewModel Create(NodeRole role)
    {
        var repositories = new RepositoriesViewModel(TestTexts.English, _api, _dialogs);
        repositories.Configure(role);
        return repositories;
    }

    /// <summary>
    /// Builds the answer of a Workstation that keeps "app" and knows "tools".
    /// </summary>
    /// <param name="active">The repository the node works with.</param>
    /// <param name="toolsFolder">The local folder of "tools", or <see langword="null"/> when it is not downloaded.</param>
    /// <param name="offered">Whether the answer knows what the Origin offers.</param>
    /// <param name="withTools">Whether "tools" is in the answer.</param>
    /// <returns>The answer.</returns>
    private static RepositoryListDto Workstation(string active, string? toolsFolder = null, bool offered = true, bool withTools = true)
    {
        var repositories = new List<RepositoryDto> { new("app", _appFolder, active == "app", true, offered ? true : null) };
        if (withTools && (toolsFolder is not null || offered))
        {
            repositories.Add(new RepositoryDto("tools", toolsFolder, active == "tools", toolsFolder is not null, offered ? true : null));
        }

        return new RepositoryListDto(NodeRole.Workstation, active, null, repositories, null);
    }

    /// <summary>
    /// Waits until a condition holds.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }
}
