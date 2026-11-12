using CliFx.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RCynQ.Cli.Commands.Node;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;
using RCynQ.Cli.Tests.Support;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Sync.Configuration.Access;

namespace RCynQ.Cli.Tests;

/// <summary>
/// Tests of <see cref="NodeResetCommand"/>: <c>rcynq node reset</c> tells what goes and what stays, and removes nothing
/// until the user named the node and the service is not running.
/// </summary>
public sealed class NodeResetCommandTests : IDisposable
{
    /// <summary>The SSH key of the Workstation the node allowed.</summary>
    private const string WORKSTATION_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:workstation-1";

    /// <summary>The temporary folder with the configuration and the data.</summary>
    private readonly string _folder = Directory.CreateTempSubdirectory("rcynq-reset-cli-").FullName;

    /// <summary>Tells whether the service runs.</summary>
    private readonly ILocalServiceProbe _service = Substitute.For<ILocalServiceProbe>();

    /// <summary>The configuration file.</summary>
    private readonly string _config;

    /// <summary>The data of the node.</summary>
    private readonly AppDataPaths _data;

    /// <summary>The authorized_keys file of the local SSH server.</summary>
    private readonly string _authorizedKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeResetCommandTests"/> class with a node that has data and a line in
    /// authorized_keys.
    /// </summary>
    public NodeResetCommandTests()
    {
        _config = Path.Combine(_folder, SyncBridgeOptions.DEFAULT_FILE_NAME);
        File.WriteAllText(_config, "{ \"SyncBridge\": { \"NodeId\": \"node-1\", \"Role\": \"Origin\", \"DataRoot\": \"data\", \"ListenPort\": 18555 } }");
        _data = AppDataPaths.FromRoot(Path.Combine(_folder, "data"));
        Directory.CreateDirectory(_data.SshKeysDirectory);
        File.WriteAllText(_data.DatabasePath, "db");
        File.WriteAllText(_data.ApiTokenPath, "token");
        _authorizedKeys = Path.Combine(_folder, "authorized_keys");
        File.WriteAllLines(_authorizedKeys, new[] { "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIHandWrittenKeyOfTheUserxxxxxxxxxxxxxxxxxxx user@laptop", $"restrict {WORKSTATION_KEY}" });
    }

    /// <summary>
    /// Verifies that the node is reset once its identifier is typed: the consequences are shown first, then the line of
    /// the node leaves authorized_keys and the configuration and the data go.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Typed_Identifier_Resets_The_Node()
    {
        var (code, output, error) = await RunAsync("node-1" + Environment.NewLine, "node", "reset", "--config", _config);

        code.Should().Be(0, error);
        output.Should()
            .Contain(TestTexts.English.Format("Cli.Reset.Header", "node-1", NodeRole.Origin))
            .And.Contain("- " + TestTexts.English.Get("Reset.Config"))
            .And.Contain(_config)
            .And.Contain(TestTexts.English.Get("Reset.Stays"))
            .And.Contain(TestTexts.English.Get("Reset.Neighbours"))
            .And.Contain(TestTexts.English.Get("Reset.Irreversible"))
            .And.Contain(TestTexts.English.Format("Cli.Reset.Prompt", "node-1"))
            .And.Contain(TestTexts.English.Format("Reset.AccessRemoved", 1, _authorizedKeys))
            .And.Contain(TestTexts.English.Format("Cli.Reset.Done", "node-1", 3), "the database, the keys and the configuration");
        File.Exists(_config).Should().BeFalse();
        Directory.Exists(_data.Root).Should().BeFalse();
        File.ReadAllLines(_authorizedKeys).Should().ContainSingle().Which.Should().EndWith("user@laptop");
    }

    /// <summary>
    /// Verifies that <c>--confirm</c> with the identifier resets the node without the question.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Confirm_Option_Resets_Without_The_Question()
    {
        var (code, output, _) = await RunAsync(string.Empty, "node", "reset", "--config", _config, "--confirm", "node-1");

        code.Should().Be(0);
        output.Should().NotContain(TestTexts.English.Format("Cli.Reset.Prompt", "node-1"));
        File.Exists(_config).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a wrong identifier, a wrong answer and no answer at all remove nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Anything_But_The_Identifier_Removes_Nothing()
    {
        var mismatch = await RunAsync(string.Empty, "node", "reset", "--config", _config, "--confirm", "node-2");
        mismatch.Code.Should().Be(2);
        mismatch.Error.Should().Contain(TestTexts.English.Format("Cli.Reset.Mismatch", "node-2", "node-1"));

        var wrong = await RunAsync("yes" + Environment.NewLine, "node", "reset", "--config", _config);
        wrong.Code.Should().Be(1);
        wrong.Error.Should().Contain(TestTexts.English.Format("Cli.Reset.Cancelled", "node-1"));

        var silent = await RunAsync(string.Empty, "node", "reset", "--config", _config);
        silent.Code.Should().Be(1);

        AssertNothingRemoved();
    }

    /// <summary>
    /// Verifies that a node whose service runs is not reset, before anything is asked.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Running_Service_Stops_The_Reset()
    {
        _service.IsRunningAsync(18555, Arg.Any<CancellationToken>()).Returns(true);

        var (code, output, error) = await RunAsync("node-1" + Environment.NewLine, "node", "reset", "--config", _config);

        code.Should().Be(1);
        error.Should().Contain(TestTexts.English.Format("Cli.Reset.ServiceRunning", 18555));
        output.Should().NotContain(TestTexts.English.Format("Cli.Reset.Prompt", "node-1"));
        AssertNothingRemoved();
    }

    /// <summary>
    /// Verifies that a missing configuration is an input error.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Missing_Configuration_Is_Reported()
    {
        var (code, _, _) = await RunAsync(string.Empty, "node", "reset", "--config", Path.Combine(_folder, "none.json"), "--confirm", "node-1");

        code.Should().Be(2);
        AssertNothingRemoved();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Checks that the node is still there.
    /// </summary>
    private void AssertNothingRemoved()
    {
        File.Exists(_config).Should().BeTrue();
        File.Exists(_data.DatabasePath).Should().BeTrue();
        File.ReadAllLines(_authorizedKeys).Should().HaveCount(2);
    }

    /// <summary>
    /// Runs the CLI with an in-memory console, the real eraser and a node context whose access service works on the test
    /// authorized_keys.
    /// </summary>
    /// <param name="input">What the user types.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>The exit code, standard output and standard error.</returns>
    private async Task<(int Code, string Output, string Error)> RunAsync(string input, params string[] args)
    {
        using var console = new FakeInMemoryConsole();
        console.WriteInput(input);
        var peers = Substitute.For<IPeerRepository>();
        peers.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new PeerDescriptor(new NodeId("workstation-1"), "workstation-1", NodeRole.Workstation, "10.8.1.3", 22, "user", sshPublicKey: WORKSTATION_KEY),
        });
        var origin = new NodeConfig(new NodeId("node-1"), "node-1", NodeRole.Origin, "/srv/repo", null);
        var factory = new TestNodeContextFactory(services => services.AddSingleton(new PeerAccessService(
            TestTexts.English, origin, new SyncBridgeOptions(), peers, new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = _authorizedKeys }))));
        var application = CliHost.CreateApplication(factory, TestTexts.English, console, services => services.AddSingleton(_service));
        var code = await application.RunAsync(args, new Dictionary<string, string>());
        return (code, console.ReadOutputString(), console.ReadErrorString());
    }
}
