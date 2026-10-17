using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Ssh.Server;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Wizard;

namespace RCynQ.UI.Tests.Wizard;

/// <summary>
/// Tests of <see cref="ConnectWizardViewModel"/> and its pages: the way through the pages, the check of the computer,
/// the waiting for an invited node and the picture of the chain.
/// </summary>
public sealed class ConnectWizardTests
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The time the tests run at.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Verifies the pages of an endpoint: the way, the check, the choice, the join page and back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Endpoint_Walks_Through_The_Pages()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshdListening: true));
        var wizard = Wizard(NodeRole.Workstation);

        wizard.Step.Should().Be(WizardStep.Scenario);
        wizard.ChainLabels.Should().Equal("Workstation", "Relay", "Origin");
        wizard.IsDirect = true;
        wizard.ChainLabels.Should().Equal("Workstation", "Origin");

        await wizard.NextCommand.ExecuteAsync(null);
        wizard.Step.Should().Be(WizardStep.Readiness);
        wizard.IsReady.Should().BeTrue();
        wizard.ContinueCommand.Execute(null);
        wizard.Step.Should().Be(WizardStep.Action);
        wizard.ActionHint.Should().Be(TestTexts.Russian.Format("Wizard.Hint.EndpointDirect", NodeRole.Origin));
        wizard.ChooseJoinCommand.Execute(null);
        wizard.Step.Should().Be(WizardStep.Join);
        wizard.BackCommand.Execute(null);
        wizard.Step.Should().Be(WizardStep.Action);
    }

    /// <summary>
    /// Verifies that a Relay starts with the check, goes straight to inviting and draws itself with the invited role.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Relay_Only_Invites()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshdListening: true));
        var wizard = Wizard(NodeRole.Relay);

        wizard.Step.Should().Be(WizardStep.Readiness);
        wizard.IsEndpoint.Should().BeFalse();
        await wizard.StartAsync();
        wizard.IsReady.Should().BeTrue("the readiness page of a Relay checks the computer as the wizard opens");
        await _api.Received(1).ProbeExportAsync(Arg.Any<CancellationToken>());
        wizard.ContinueCommand.Execute(null);

        wizard.Step.Should().Be(WizardStep.Invite);
        wizard.ChainLabels.Should().Equal("Relay", "Origin");
        wizard.Invite.SelectedRole = NodeRole.Workstation;
        wizard.ChainLabels.Should().Equal("Relay", "Workstation");
    }

    /// <summary>
    /// Verifies that an SSH server that does not answer is reported and the installation is offered.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Computer_That_Takes_Nobody_Is_Offered_The_Ssh_Server()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshdListening: false));
        var wizard = Wizard(NodeRole.Origin, Substitute.For<IServerSetup>());

        await wizard.NextCommand.ExecuteAsync(null);

        wizard.IsReady.Should().BeFalse();
        wizard.ReadinessText.Should().Be(TestTexts.Russian.Format("Export.Ssh.Closed", 22));
        wizard.Readiness.CanInstallSshServer.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the wizard waits for the invited node, asks for administrator rights for the one-time key, and ends
    /// when the node joined with every link of the picture finished.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Invitation_Waits_Until_The_Node_Joins()
    {
        var id = Guid.NewGuid();
        var open = new InvitationDto(id, NodeRole.Workstation, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), null, null, false);
        _api.CreateInvitationAsync(Arg.Any<CreateInvitationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreatedInvitation("rcynq-invite:abc", open, true, Array.Empty<string>()));
        _api.GetInvitationsAsync(Arg.Any<CancellationToken>()).Returns(
            new[] { open },
            new[] { open with { UsedBy = "workstation-1", UsedAtUtc = DateTimeOffset.UnixEpoch } });
        var allowed = 0;
        var wizard = Wizard(NodeRole.Origin, allowAccess: () =>
        {
            allowed++;
            return Task.CompletedTask;
        });
        wizard.IsDirect = true;
        wizard.ChooseInviteCommand.Execute(null);

        await wizard.Invite.CreateCommand.ExecuteAsync(null);
        await wizard.Invite.Waiting;

        allowed.Should().Be(1, "the one-time key waits for administrator rights");
        await _api.Received(2).GetInvitationsAsync(Arg.Any<CancellationToken>());
        wizard.Step.Should().Be(WizardStep.Done);
        wizard.DoneMessage.Should().Be(TestTexts.Russian.Format("Wizard.Joined", "workstation-1"));
        wizard.CompletedLinks.Should().Be(wizard.LinkCount);
        wizard.ChainActive.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an invitation that expires before a node joins is put away with a message, the waiting stops and a
    /// new invitation can be created.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Expired_Invitation_Can_Be_Made_Again()
    {
        var open = new InvitationDto(Guid.NewGuid(), NodeRole.Workstation, _now, _now.AddHours(1), null, null, false);
        _api.CreateInvitationAsync(Arg.Any<CreateInvitationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreatedInvitation("rcynq-invite:abc", open, false, Array.Empty<string>()));
        _api.GetInvitationsAsync(Arg.Any<CancellationToken>()).Returns(new[] { open });
        var clock = new FixedClock(_now.AddHours(2));
        var wizard = new ConnectWizardViewModel(TestTexts.Russian, _api, _dialogs, NodeRole.Origin, "node-1", delay: (_, _) => Task.CompletedTask, time: clock);
        wizard.ChooseInviteCommand.Execute(null);

        await wizard.Invite.CreateCommand.ExecuteAsync(null);
        await wizard.Invite.Waiting;

        wizard.Step.Should().Be(WizardStep.Invite);
        wizard.Invite.HasInvitation.Should().BeFalse();
        wizard.Invite.CanCreate.Should().BeTrue();
        wizard.Invite.Error.Should().Be(TestTexts.Russian.Get("Invite.Closed"));
        wizard.ChainActive.Should().BeFalse();
        wizard.CanGoBack.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the picture after joining: through a Relay every link is finished; with the invitation of the Relay
    /// itself only the first one, and the wizard offers to invite the counterpart next.
    /// </summary>
    /// <param name="relayStage">Whether the joining had a Relay stage (the invitation came from the counterpart).</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Joining_Finishes_The_Links_It_Made(bool relayStage)
    {
        var steps = new List<JoinStepResult> { new(JoinStep.Invitation, true, "ok"), new(JoinStep.Nodes, true, "ok") };
        if (relayStage)
        {
            steps.Add(new JoinStepResult(JoinStep.Relay, true, "relay"));
        }

        steps.Add(new JoinStepResult(JoinStep.Inviter, true, "in"));
        _api.JoinAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new JoinResult(true, "joined", steps));
        var wizard = Wizard(NodeRole.Origin);
        wizard.ChooseJoinCommand.Execute(null);
        wizard.Join.Invitation = "rcynq-invite:abc";

        await wizard.Join.JoinCommand.ExecuteAsync(null);

        wizard.Step.Should().Be(WizardStep.Done);
        wizard.CompletedLinks.Should().Be(relayStage ? 2 : 1);
        wizard.CanInviteNext.Should().Be(!relayStage);
        if (!relayStage)
        {
            wizard.InviteNextCommand.Execute(null);
            wizard.Step.Should().Be(WizardStep.Invite);
            wizard.Invite.SelectedRole.Should().Be(NodeRole.Workstation);
        }
    }

    /// <summary>
    /// Verifies that a refusal of the inviting node marks the last link red and keeps the join page.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refusal_Marks_The_Failed_Link()
    {
        var steps = new[] { new JoinStepResult(JoinStep.Invitation, true, "ok"), new JoinStepResult(JoinStep.Inviter, false, "refused") };
        _api.JoinAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new JoinResult(false, "failed", steps));
        var wizard = Wizard(NodeRole.Workstation);
        wizard.ChooseJoinCommand.Execute(null);
        wizard.Join.Invitation = "rcynq-invite:abc";

        await wizard.Join.JoinCommand.ExecuteAsync(null);

        wizard.Step.Should().Be(WizardStep.Join);
        wizard.FailedLink.Should().Be(wizard.LinkCount - 1);
        wizard.Join.Steps.Should().HaveCount(2);
        wizard.CanGoBack.Should().BeTrue("a failed joining can be tried another way");
    }

    /// <summary>
    /// Verifies that the stages the service reports while joining runs move the bytes to the link worked on, finish it,
    /// and list the stages as running and then with their outcome.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Live_Stages_Move_Along_The_Chain()
    {
        var answer = new TaskCompletionSource<JoinResult>();
        _api.JoinAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(answer.Task);
        var wizard = Wizard(NodeRole.Workstation);
        wizard.ChooseJoinCommand.Execute(null);
        wizard.Join.Invitation = "rcynq-invite:abc";

        var joining = wizard.Join.JoinCommand.ExecuteAsync(null);
        wizard.ChainActive.Should().BeTrue("nothing is known yet, so the whole chain moves");

        wizard.ApplyJoinProgress(new JoinProgressEvent(JoinStep.Invitation, JoinStepState.Succeeded, "ok", NodeRole.Origin));
        wizard.ApplyJoinProgress(new JoinProgressEvent(JoinStep.Relay, JoinStepState.Running, null, NodeRole.Origin));
        wizard.ActiveLink.Should().Be(0);
        wizard.ChainActive.Should().BeFalse();
        wizard.Join.Steps[^1].IsRunning.Should().BeTrue();

        wizard.ApplyJoinProgress(new JoinProgressEvent(JoinStep.Relay, JoinStepState.Succeeded, "relay", NodeRole.Origin));
        wizard.ApplyJoinProgress(new JoinProgressEvent(JoinStep.Inviter, JoinStepState.Running, null, NodeRole.Origin));
        wizard.CompletedLinks.Should().Be(1);
        wizard.ActiveLink.Should().Be(1, "the Origin lies behind the Relay");
        wizard.Join.Steps.Select(step => step.Step).Should().Equal(JoinStep.Invitation, JoinStep.Relay, JoinStep.Inviter);

        wizard.ApplyJoinProgress(new JoinProgressEvent(JoinStep.Inviter, JoinStepState.Failed, "refused", NodeRole.Origin));
        wizard.FailedLink.Should().Be(1);
        wizard.ActiveLink.Should().Be(-1);
        wizard.Join.Steps[^1].HasFailed.Should().BeTrue();

        answer.SetResult(new JoinResult(false, "failed", new[]
        {
            new JoinStepResult(JoinStep.Invitation, true, "ok"),
            new JoinStepResult(JoinStep.Relay, true, "relay"),
            new JoinStepResult(JoinStep.Inviter, false, "refused")
        }));
        await joining;
        wizard.FailedLink.Should().Be(1);
        wizard.Join.Steps.Select(step => step.Step).Should().Equal(
            new[] { JoinStep.Invitation, JoinStep.Relay, JoinStep.Inviter },
            "the answer replaces the stages the live reports showed instead of adding them a second time");
    }

    /// <summary>
    /// Verifies that a joining of the invitation of the Relay itself finishes the first link and that reports outside a
    /// joining are ignored.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Joining_The_Relay_Works_On_The_First_Link()
    {
        var answer = new TaskCompletionSource<JoinResult>();
        _api.JoinAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(answer.Task);
        var wizard = Wizard(NodeRole.Origin);
        wizard.ApplyJoinProgress(new JoinProgressEvent(JoinStep.Inviter, JoinStepState.Running, null, NodeRole.Relay));
        wizard.ActiveLink.Should().Be(-1, "no joining runs");

        wizard.ChooseJoinCommand.Execute(null);
        wizard.Join.Invitation = "rcynq-invite:abc";
        var joining = wizard.Join.JoinCommand.ExecuteAsync(null);
        wizard.ApplyJoinProgress(new JoinProgressEvent(JoinStep.Inviter, JoinStepState.Running, null, NodeRole.Relay));
        wizard.ActiveLink.Should().Be(0);
        wizard.ApplyJoinProgress(new JoinProgressEvent(JoinStep.Inviter, JoinStepState.Succeeded, "in", NodeRole.Relay));

        answer.SetResult(new JoinResult(true, "joined", new[] { new JoinStepResult(JoinStep.Inviter, true, "in") }));
        await joining;

        wizard.CompletedLinks.Should().Be(1);
        wizard.CanInviteNext.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the note on an administrator account: for a Relay it advises an ordinary account.
    /// </summary>
    /// <param name="role">The role of this node.</param>
    /// <param name="key">The expected note.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(NodeRole.Relay, "Wizard.Admin.Relay")]
    [InlineData(NodeRole.Origin, "Wizard.Admin.Endpoint")]
    public async Task Administrator_Account_Is_Explained(NodeRole role, string key)
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshdListening: true) with
        {
            Warnings = new[] { EnvironmentWarning.AccessNeedsAdministrator }
        });
        var wizard = Wizard(role);

        await wizard.CheckCommand.ExecuteAsync(null);

        wizard.HasAdminNote.Should().BeTrue();
        wizard.AdminNote.Should().Be(TestTexts.Russian.Get(key));
    }

    /// <summary>
    /// Creates the wizard; the waiting between looks at the invitations takes no time.
    /// </summary>
    /// <param name="role">The role of this node.</param>
    /// <param name="setup">The setup of the SSH server, or <see langword="null"/>.</param>
    /// <param name="allowAccess">Asks for administrator rights, or <see langword="null"/>.</param>
    /// <returns>The wizard.</returns>
    private ConnectWizardViewModel Wizard(NodeRole role, IServerSetup? setup = null, Func<Task>? allowAccess = null)
        => new(TestTexts.Russian, _api, _dialogs, role, "node-1", setup, allowAccess, (_, _) => Task.CompletedTask, new FixedClock(DateTimeOffset.UnixEpoch));

    /// <summary>
    /// Creates a probe report of a node behind its SSH server.
    /// </summary>
    /// <param name="sshdListening">Whether the SSH server answers.</param>
    /// <returns>The report.</returns>
    private static NodeEnvironmentReport Report(bool sshdListening)
        => new(
            "PC", "dev", 22, sshdListening, true,
            new[] { new NetworkAddressInfo("10.8.0.3", "awg0", NetworkAddressKind.Tunnel, sshdListening) },
            "10.8.0.3", null, null, null, Array.Empty<string>(), Array.Empty<EnvironmentWarning>(), DateTimeOffset.UnixEpoch);

    /// <summary>
    /// A clock that stands at one moment.
    /// </summary>
    private sealed class FixedClock : TimeProvider
    {
        /// <summary>The moment.</summary>
        private readonly DateTimeOffset _now;

        /// <summary>
        /// Initializes a new instance of the <see cref="FixedClock"/> class.
        /// </summary>
        /// <param name="now">The moment.</param>
        public FixedClock(DateTimeOffset now) => _now = now;

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
