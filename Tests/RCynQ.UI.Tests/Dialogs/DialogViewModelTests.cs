using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.UI.Services.Api;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Items;
using Xunit;

namespace RCynQ.UI.Tests.Dialogs;

/// <summary>
/// Tests of the dialog view models.
/// </summary>
public sealed class DialogViewModelTests
{
    /// <summary>
    /// Verifies that each conflict choice records its policy and closes the dialog.
    /// </summary>
    [Fact]
    public void Conflict_Choices_Close_With_Policy()
    {
        var dialog = new ConflictDialogViewModel(Guid.NewGuid(), "dest-1", Array.Empty<ConflictFile>());
        bool? closed = null;
        dialog.CloseRequested += (_, confirmed) => closed = confirmed;

        dialog.UseGitCommand.Execute(null);
        dialog.Result.Should().Be(ConflictPolicy.UseGit);
        closed.Should().BeTrue();

        dialog.KeepLocalCommand.Execute(null);
        dialog.Result.Should().Be(ConflictPolicy.KeepLocal);

        dialog.CancelCommand.Execute(null);
        dialog.Result.Should().BeNull();
        closed.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that option injection through the host is rejected before anything is saved.
    /// </summary>
    [Fact]
    public void AddNode_Rejects_Unsafe_Host()
    {
        var dialog = new AddNodeDialogViewModel { NodeId = "mediator-1", Host = "-oProxyCommand=calc", SshUser = "jump" };
        var closed = false;
        dialog.CloseRequested += (_, _) => closed = true;

        dialog.ConfirmCommand.Execute(null);

        closed.Should().BeFalse();
        dialog.Error.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Verifies that an invalid node identifier is reported instead of thrown.
    /// </summary>
    [Fact]
    public void AddNode_Rejects_Invalid_NodeId()
    {
        var dialog = new AddNodeDialogViewModel { NodeId = "bad id/..", Host = "203.0.113.4", SshUser = "jump" };

        dialog.ConfirmCommand.Execute(null);

        dialog.Confirmed.Should().BeFalse();
        dialog.Error.Should().Be("Недопустимый идентификатор узла.");
    }

    /// <summary>
    /// Verifies the link settings produced by a valid AmneziaWG node.
    /// </summary>
    [Fact]
    public void AddNode_Builds_Link_Options()
    {
        var dialog = new AddNodeDialogViewModel
        {
            NodeId = "dest-1",
            Role = NodeRole.Dest,
            Host = "10.8.0.2",
            SshUser = "rcynq",
            SelectedTransport = TransportOption.For(TransportKind.AmneziaWG),
            ConfigPath = "/etc/amnezia/amneziawg/awg0.conf"
        };

        dialog.Transport.Should().Be(TransportKind.AmneziaWG);
        dialog.NeedsConfigPath.Should().BeTrue();
        TransportOption.For(TransportKind.Direct).Title.Should().Be("Обычная сеть (без VPN)", "'Direct' means no VPN tunnel, not 'no SSH'");

        dialog.ConfirmCommand.Execute(null);

        dialog.Confirmed.Should().BeTrue();
        var options = dialog.ToOptions();
        options.RemotePort.Should().Be(18443, "a Dest peer is reached on its service port");
        options.Priority.Should().Be(1000);
        options.TransportSettings.Should().Contain("configPath", "/etc/amnezia/amneziawg/awg0.conf");
    }

    /// <summary>
    /// Verifies the export dialog validation.
    /// </summary>
    [Fact]
    public void Export_Validates_Host()
    {
        var dialog = new ExportDialogViewModel(Substitute.For<IServiceApiClient>()) { Host = "bad host;rm", User = "rcynq" };

        dialog.ConfirmCommand.Execute(null);
        dialog.Confirmed.Should().BeFalse();

        dialog.Host = "10.8.0.1";
        dialog.ConfirmCommand.Execute(null);
        dialog.Confirmed.Should().BeTrue();
        dialog.Error.Should().BeNull();
    }
}
