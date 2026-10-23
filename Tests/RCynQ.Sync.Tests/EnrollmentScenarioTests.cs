using System.Security.Cryptography;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Git.Processes;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Access;
using RCynQ.Sync.Configuration.Probing;
using RCynQ.Sync.Enrollment;
using RCynQ.Sync.Security;
using RCynQ.Sync.Tests.Fakes;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Sets up a Relay, an Origin and a Workstation with two invitations, the way a person does it, with the nodes in one
/// process: the enrollment of a node reaches the <see cref="EnrollmentService"/> of the node at the end of its chain.
/// The one-time keys are made by the installed <c>ssh-keygen</c>.
/// </summary>
public sealed class EnrollmentScenarioTests : IDisposable
{
    /// <summary>The SSH keys of the nodes, one each.</summary>
    private static readonly Dictionary<NodeRole, string> _sshKeys = new()
    {
        [NodeRole.Relay] = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIE2mfktackE6lnW8Hp/QiIkF7Dp5hFWxNzR5c9wXgKja rcynq:relay-1",
        [NodeRole.Origin] = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPBzgtLuBSnvmhyPyE/aR3fT7G5Ynge9cP0KZC9vUVUb rcynq:origin-1",
        [NodeRole.Workstation] = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:workstation-1"
    };

    /// <summary>The clock of all three nodes.</summary>
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The temporary folder of the nodes.</summary>
    private readonly string _root = Directory.CreateTempSubdirectory("rcynq-enroll-scenario-").FullName;

    /// <summary>The nodes by identifier, which the channel delivers to.</summary>
    private readonly Dictionary<NodeId, TestNode> _nodes = new();

    /// <summary>
    /// Stands for the network between the nodes: gets the node an enrollment goes to and the delivery itself, and may
    /// change the answer or fail instead; <see langword="null"/> delivers every enrollment as it is.
    /// </summary>
    private Func<NodeId, Func<Task<EnrollmentResponse>>, Task<EnrollmentResponse>>? _network;

    /// <summary>
    /// Verifies the whole setup through a Relay: the Relay invites the Origin, the Origin invites the Workstation with
    /// the ticket the Relay gave it, and in the end every node knows its neighbours, the Workstation has the link token
    /// of the Origin, and the invitations cannot be used again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Two_Invitations_Set_Up_The_Chain_Through_A_Relay()
    {
        var relay = Node("relay-1", NodeRole.Relay, "203.0.113.4");
        var origin = Node("origin-1", NodeRole.Origin, "10.20.0.5", "origin-token-0123456789");
        var workstation = Node("workstation-1", NodeRole.Workstation, "10.30.0.7", "own-token-of-the-workstation");

        var forOrigin = await relay.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Origin, 24), relay.ConfigPath);
        var originJoined = await origin.Join.JoinAsync(forOrigin.Text, origin.ConfigPath);
        originJoined.Joined.Should().BeTrue(string.Join(" | ", originJoined.Steps.Select(step => step.Message)));

        var forWorkstation = await origin.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Workstation, 24), origin.ConfigPath);
        forWorkstation.Notes.Should().BeEmpty("the Origin holds the ticket of its Relay");
        var progress = new List<JoinProgressEvent>();
        var joined = await workstation.Join.JoinAsync(forWorkstation.Text, workstation.ConfigPath, evt =>
        {
            progress.Add(evt);
            return Task.CompletedTask;
        });

        joined.Joined.Should().BeTrue(string.Join(" | ", joined.Steps.Select(step => step.Message)));
        progress.Select(evt => $"{evt.Step}:{evt.State}").Should().Equal(
            "Invitation:Running", "Invitation:Succeeded", "Nodes:Running", "Nodes:Succeeded", "Relay:Running", "Relay:Succeeded",
            "Inviter:Running", "Inviter:Succeeded", "Access:Running", "Access:Succeeded");
        progress[0].Inviter.Should().BeNull("the invitation is not read yet");
        progress.Skip(1).Should().OnlyContain(evt => evt.Inviter == NodeRole.Origin);
        joined.Steps.Select(step => step.Step).Should().Equal(JoinStep.Invitation, JoinStep.Nodes, JoinStep.Relay, JoinStep.Inviter, JoinStep.Access);
        relay.KnownPeers.Should().BeEquivalentTo("origin-1", "workstation-1");
        origin.KnownPeers.Should().BeEquivalentTo("relay-1", "workstation-1");
        workstation.KnownPeers.Should().BeEquivalentTo("relay-1", "origin-1");
        workstation.Options.LinkToken.Should().Be("origin-token-0123456789");
        (await File.ReadAllTextAsync(workstation.ConfigPath)).Should().Contain("origin-token-0123456789");

        var again = await Node("workstation-2", NodeRole.Workstation, "10.30.0.8").Join.JoinAsync(forWorkstation.Text, _root + "/w2.json");
        again.Joined.Should().BeFalse();
        again.Steps[^1].Message.Should().Be(TestTexts.English.Get("Enrollment.TicketRefused"));
    }

    /// <summary>
    /// Verifies that joining through a Relay that took the node but has not let it through yet waits for the Relay and
    /// then joins in one go, instead of asking the person to use the invitation once more.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Joining_Waits_For_A_Relay_That_Still_Has_To_Allow_The_Node()
    {
        var (origin, workstation, invitation) = await OriginInvitesWorkstationAsync();
        var blocked = 2;
        _network = async (target, deliver) =>
        {
            if (target.Value == "relay-1")
            {
                return await deliver() with { AccessPending = true };
            }

            if (blocked-- > 0)
            {
                throw new SyncBridgeException("Permission denied (publickey).");
            }

            return await deliver();
        };

        var joined = await workstation.Join.JoinAsync(invitation, workstation.ConfigPath);

        joined.Joined.Should().BeTrue(string.Join(" | ", joined.Steps.Select(step => step.Message)));
        blocked.Should().Be(-1, "the inviting node was tried until the Relay let the node through");
        joined.Steps.Should().OnlyContain(step => step.Succeeded);
        joined.Steps.Single(step => step.Step == JoinStep.Relay).Message.Should().Be(TestTexts.English.Format("Join.RelayWaiting", "relay-1"));
        origin.KnownPeers.Should().Contain("workstation-1");
    }

    /// <summary>
    /// Verifies that joining stops waiting for a Relay that never lets the node through, and says what to do.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Joining_Gives_Up_On_A_Relay_That_Never_Allows_The_Node()
    {
        var (origin, workstation, invitation) = await OriginInvitesWorkstationAsync();
        _network = async (target, deliver) =>
        {
            if (target.Value == "relay-1")
            {
                return await deliver() with { AccessPending = true };
            }

            _clock.Advance(TimeSpan.FromMinutes(1));
            throw new SyncBridgeException("Permission denied (publickey).");
        };

        var joined = await workstation.Join.JoinAsync(invitation, workstation.ConfigPath);

        joined.Joined.Should().BeFalse();
        joined.Steps[^1].Should().Match<JoinStepResult>(step => step.Step == JoinStep.Inviter && !step.Succeeded);
        joined.Steps[^1].Message.Should().StartWith(TestTexts.English.Format("Join.RelayPending", "relay-1")).And.Contain("Permission denied");
        origin.KnownPeers.Should().NotContain("workstation-1");
    }

    /// <summary>
    /// Verifies that a Relay which refuses its used ticket because it knows the node already does not show as a failed
    /// stage once the joining went through it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Relay_That_Knows_The_Node_Already_Is_Not_A_Failed_Stage()
    {
        var (_, workstation, invitation) = await OriginInvitesWorkstationAsync();
        _network = async (target, deliver) => target.Value == "relay-1"
            ? new EnrollmentResponse(false, TestTexts.English.Get("Enrollment.TicketRefused"))
            : await deliver();

        var joined = await workstation.Join.JoinAsync(invitation, workstation.ConfigPath);

        joined.Joined.Should().BeTrue();
        joined.Steps.Should().OnlyContain(step => step.Succeeded);
        joined.Steps.Single(step => step.Step == JoinStep.Relay).Message.Should().Be(TestTexts.English.Format("Join.RelayKnown", "relay-1"));
    }

    /// <summary>
    /// Sets up a Relay and an Origin that joined it, and lets the Origin invite a Workstation.
    /// </summary>
    /// <returns>The Origin, the Workstation and the invitation for it.</returns>
    private async Task<(TestNode Origin, TestNode Workstation, string Invitation)> OriginInvitesWorkstationAsync()
    {
        var relay = Node("relay-1", NodeRole.Relay, "203.0.113.4");
        var origin = Node("origin-1", NodeRole.Origin, "10.20.0.5", "origin-token-0123456789");
        var workstation = Node("workstation-1", NodeRole.Workstation, "10.30.0.7");
        var forOrigin = await relay.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Origin, 24), relay.ConfigPath);
        (await origin.Join.JoinAsync(forOrigin.Text, origin.ConfigPath)).Joined.Should().BeTrue();
        var forWorkstation = await origin.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Workstation, 24), origin.ConfigPath);
        return (origin, workstation, forWorkstation.Text);
    }

    /// <summary>
    /// Verifies that the one-time key of an open invitation is in <c>authorized_keys</c> with its forced command, and that
    /// it is gone once the invitation is used.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task One_Time_Key_Lives_While_The_Invitation_Is_Open()
    {
        var relay = Node("relay-1", NodeRole.Relay, "203.0.113.4");
        var origin = Node("origin-1", NodeRole.Origin, "10.20.0.5", "origin-token-0123456789");

        var invitation = await relay.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Origin, 1), relay.ConfigPath);
        var line = (await File.ReadAllLinesAsync(relay.AuthorizedKeysPath)).Should().ContainSingle().Subject;
        line.Should().StartWith("restrict,expiry-time=\"202610161300\",command=").And.Contain("enroll").And.Contain(AuthorizedKeysManager.ENROLLMENT_COMMENT_PREFIX);

        (await origin.Join.JoinAsync(invitation.Text, origin.ConfigPath)).Joined.Should().BeTrue();

        var lines = await File.ReadAllLinesAsync(relay.AuthorizedKeysPath);
        lines.Should().NotContain(entry => entry.EndsWith("rcynq:origin-1", StringComparison.Ordinal), "a Relay lets an endpoint through only to the opposite one, which it does not know yet");
        lines.Where(entry => entry.Contains(invitation.Summary.Id.ToString("N"), StringComparison.Ordinal)).Should().BeEmpty("the used key is removed");
        lines.Should().ContainSingle(entry => entry.Contains(AuthorizedKeysManager.ENROLLMENT_COMMENT_PREFIX, StringComparison.Ordinal), "the ticket for the Workstation is open");
    }

    /// <summary>
    /// Verifies the refusals: an invitation for another role, an expired one and a revoked one never enroll anything.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Wrong_Expired_And_Revoked_Invitations_Are_Refused()
    {
        var relay = Node("relay-1", NodeRole.Relay, "203.0.113.4");
        var origin = Node("origin-1", NodeRole.Origin, "10.20.0.5", "origin-token-0123456789");
        var workstation = Node("workstation-1", NodeRole.Workstation, "10.30.0.7");

        var forOrigin = await relay.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Origin, 1), relay.ConfigPath);
        var wrongRole = await workstation.Join.JoinAsync(forOrigin.Text, workstation.ConfigPath);
        wrongRole.Steps.Should().ContainSingle().Which.Step.Should().Be(JoinStep.Invitation);
        workstation.KnownPeers.Should().BeEmpty();

        (await relay.Invitations.RevokeAsync(forOrigin.Summary.Id)).Should().BeTrue();
        var revoked = await origin.Join.JoinAsync(forOrigin.Text, origin.ConfigPath);
        revoked.Steps[^1].Should().Match<JoinStepResult>(step => step.Step == JoinStep.Inviter && !step.Succeeded);

        var later = await relay.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Origin, 1), relay.ConfigPath);
        _clock.Advance(TimeSpan.FromHours(2));
        var expired = await origin.Join.JoinAsync(later.Text, origin.ConfigPath);
        expired.Steps.Should().ContainSingle().Which.Message.Should().Be(TestTexts.English.Get("Join.Expired"));
        relay.KnownPeers.Should().NotContain("origin-1");
    }

    /// <summary>
    /// Verifies the checks of the inviting node: a role it does not invite and a lifetime out of range.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Invitations_That_Make_No_Sense_Are_Not_Created()
    {
        var origin = Node("origin-1", NodeRole.Origin, "10.20.0.5", "origin-token-0123456789");

        var relayRole = () => origin.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Origin, 24), origin.ConfigPath);
        var tooLong = () => origin.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Workstation, InvitationService.MAX_LIFETIME_HOURS + 1), origin.ConfigPath);

        await relayRole.Should().ThrowAsync<ArgumentException>();
        await tooLong.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies that a node whose command line tool lies at a path that cannot go into <c>authorized_keys</c> refuses to
    /// invite with a clear message and leaves no open ticket behind, which would stop every later update of the file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Invitation_Is_Not_Created_When_The_Tool_Path_Cannot_Be_Quoted()
    {
        var unsafePath = OperatingSystem.IsWindows() ? @"C:\100%\rcynq.exe" : "/opt/it's/rcynq";
        var relay = Node("relay-1", NodeRole.Relay, "203.0.113.4", cli: unsafePath);

        var create = () => relay.Invitations.CreateAsync(new CreateInvitationRequest(NodeRole.Origin, 1), relay.ConfigPath);

        (await create.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain(unsafePath);
        (await relay.Invitations.ListAsync()).Should().BeEmpty("no ticket is issued");
        File.Exists(relay.AuthorizedKeysPath).Should().BeFalse();
    }

    /// <summary>
    /// Deletes the temporary folder.
    /// </summary>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// Creates a node and registers it with the channel.
    /// </summary>
    /// <param name="id">The node identifier.</param>
    /// <param name="role">The role.</param>
    /// <param name="address">The address it is reachable at.</param>
    /// <param name="linkToken">The link token of an endpoint, or <see langword="null"/>.</param>
    /// <param name="cli">The path of the command line tool the SSH server runs for an enrollment.</param>
    /// <returns>The node.</returns>
    private TestNode Node(string id, NodeRole role, string address, string? linkToken = null, string cli = "/opt/rcynq/cli/rcynq")
    {
        var node = new TestNode(this, id, role, address, linkToken, cli);
        _nodes[node.Node.Id] = node;
        return node;
    }

    /// <summary>
    /// Delivers an enrollment to the node at the end of the chain, as the network would.
    /// </summary>
    private sealed class InProcessChannel : IEnrollmentChannel
    {
        /// <summary>The test, which owns the nodes and the way the network behaves.</summary>
        private readonly EnrollmentScenarioTests _test;

        /// <summary>
        /// Initializes a new instance of the <see cref="InProcessChannel"/> class.
        /// </summary>
        /// <param name="test">The test, which owns the nodes and the way the network behaves.</param>
        public InProcessChannel(EnrollmentScenarioTests test) => _test = test;

        /// <inheritdoc />
        public Task<EnrollmentResponse> SendAsync(IReadOnlyList<ChainLinkConfig> chain, InvitationTicket ticket, EnrollmentRequest request, CancellationToken cancellationToken = default)
        {
            var target = ChainRoute.Resolve(chain).Endpoint.Peer;
            target.EndpointMode.Should().Be(EndpointMode.SshTunnel);
            ticket.EnrollmentKey.Should().StartWith("-----BEGIN OPENSSH PRIVATE KEY-----", "a node with an SSH server is reached with the one-time key");
            Task<EnrollmentResponse> Deliver() => _test._nodes[target.Id].Enrollment.EnrollAsync(request, cancellationToken);
            return _test._network is { } network ? network(target.Id, Deliver) : Deliver();
        }
    }

    /// <summary>
    /// One node with all its services, its stores in memory and its files in the temporary folder.
    /// </summary>
    private sealed class TestNode
    {
        /// <summary>The links and peers of the node.</summary>
        private readonly InMemoryChainStore _store = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="TestNode"/> class.
        /// </summary>
        /// <param name="test">The test, which owns the clock, the folder and the channel.</param>
        /// <param name="id">The node identifier.</param>
        /// <param name="role">The role.</param>
        /// <param name="address">The address it is reachable at.</param>
        /// <param name="linkToken">The link token, or <see langword="null"/>.</param>
        /// <param name="cli">The path of the command line tool the SSH server runs for an enrollment.</param>
        public TestNode(EnrollmentScenarioTests test, string id, NodeRole role, string address, string? linkToken, string cli)
        {
            var folder = Directory.CreateDirectory(Path.Combine(test._root, id)).FullName;
            ConfigPath = Path.Combine(folder, "syncbridge.json");
            AuthorizedKeysPath = Path.Combine(folder, "authorized_keys");
            Node = new NodeConfig(new NodeId(id), id, role, role == NodeRole.Origin ? "/srv/repo" : null, role == NodeRole.Workstation ? "/work" : null);
            Options = new SyncBridgeOptions { NodeId = id, Role = role, ListenPort = 18443, LinkToken = linkToken };

            var texts = TestTexts.English;
            var signatures = Substitute.For<ISignatureService>();
            using (var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var exported = signingKey.ExportSubjectPublicKeyInfo();
                signatures.ExportPublicKeyAsync(Arg.Any<CancellationToken>()).Returns(exported);
            }

            var appData = Substitute.For<IAppDataFactory>();
            appData.Create().Returns(AppDataPaths.FromRoot(folder));
            var probe = Substitute.For<INodeEnvironmentProbe>();
            probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(new NodeEnvironmentReport(
                id, "rcynq", 22, true, true,
                new[] { new NetworkAddressInfo(address, "eth0", NetworkAddressKind.Private, true) },
                address, _sshKeys[role], null, null,
                new[] { "SHA256:" + Convert.ToBase64String(new byte[32]).TrimEnd('=') },
                Array.Empty<EnvironmentWarning>(), test._clock.GetUtcNow()));
            var command = Substitute.For<IEnrollmentCommandSource>();
            command.GetCommand().Returns(new[] { cli, "enroll", "--config", ConfigPath });

            var tickets = new InMemoryEnrollmentTicketRepository();
            var secrets = new InMemorySecretStore();
            var transfer = new NodeConfigurationTransfer(texts, Node, Options, signatures, _store, _store, test._clock);
            var access = new PeerAccessService(
                texts, Node, Options, _store, new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = AuthorizedKeysPath }), _store, tickets, command, test._clock);
            var issuer = new TicketIssuer(texts, Options, tickets, new SshKeyGenerator(texts, new SystemProcessRunner(texts), new SshClientOptions()), appData, test._clock, command);
            var cards = new LocalCardBuilder(probe, transfer);
            var tokens = new LinkTokenProvider(Options, secrets);
            var relayTickets = new RelayTicketStore(secrets);

            Invitations = new InvitationService(texts, Node, issuer, tickets, _store, cards, tokens, relayTickets, access, test._clock);
            Enrollment = new EnrollmentService(texts, Node, tickets, transfer, issuer, access, test._clock);
            Join = new JoinService(texts, Node, transfer, _store, cards, new InProcessChannel(test), relayTickets, tokens, access, test._clock, TimeSpan.FromMilliseconds(1));
        }

        /// <summary>Gets the node.</summary>
        public NodeConfig Node { get; }

        /// <summary>Gets the options of the node.</summary>
        public SyncBridgeOptions Options { get; }

        /// <summary>Gets the configuration file of the node.</summary>
        public string ConfigPath { get; }

        /// <summary>Gets the authorized_keys file of the node.</summary>
        public string AuthorizedKeysPath { get; }

        /// <summary>Gets the invitations of the node.</summary>
        public InvitationService Invitations { get; }

        /// <summary>Gets the enrollment of the node.</summary>
        public EnrollmentService Enrollment { get; }

        /// <summary>Gets the joining of the node.</summary>
        public JoinService Join { get; }

        /// <summary>Gets the identifiers of the peers the node is linked to.</summary>
        public IReadOnlyList<string> KnownPeers => _store.Links.Select(link => link.Peer.Id.Value).ToArray();
    }
}
