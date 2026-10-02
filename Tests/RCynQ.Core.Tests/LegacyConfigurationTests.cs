using System.Text.Json;
using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Config.Legacy;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Tests;

/// <summary>
/// Tests for what an earlier version left behind: its role names in configuration files and in JSON.
/// </summary>
public sealed class LegacyConfigurationTests : IDisposable
{
    /// <summary>The temporary directory of the test.</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rcynq-legacy-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyConfigurationTests"/> class.
    /// </summary>
    public LegacyConfigurationTests() => Directory.CreateDirectory(_directory);

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// Verifies the translation of the role names of earlier versions, whatever their letter case.
    /// </summary>
    /// <param name="name">The earlier name.</param>
    /// <param name="expected">The role it stands for.</param>
    [Theory]
    [InlineData("Source", NodeRole.Workstation)]
    [InlineData("mediator", NodeRole.Relay)]
    [InlineData(" DEST ", NodeRole.Origin)]
    public void Earlier_Role_Names_Are_Translated(string name, NodeRole expected)
    {
        LegacyRoleNames.TryTranslate(name, out var role).Should().BeTrue();
        role.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that current names and unknown names are not treated as names of earlier versions.
    /// </summary>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData("Workstation")]
    [InlineData("Relay")]
    [InlineData("Origin")]
    [InlineData("Destination")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_Names_Are_Not_Translated(string? name)
    {
        LegacyRoleNames.TryTranslate(name, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a role is read from JSON by its current name, its earlier name and its number, and always written
    /// by its current name.
    /// </summary>
    /// <param name="json">The JSON token.</param>
    /// <param name="expected">The role.</param>
    [Theory]
    [InlineData("\"Origin\"", NodeRole.Origin)]
    [InlineData("\"origin\"", NodeRole.Origin)]
    [InlineData("\"Dest\"", NodeRole.Origin)]
    [InlineData("\"Source\"", NodeRole.Workstation)]
    [InlineData("\"Mediator\"", NodeRole.Relay)]
    [InlineData("2", NodeRole.Relay)]
    public void Role_Is_Read_From_Current_And_Earlier_Names(string json, NodeRole expected)
    {
        JsonSerializer.Deserialize<NodeRole>(json).Should().Be(expected);
        JsonSerializer.Serialize(expected).Should().Be($"\"{expected}\"");
    }

    /// <summary>
    /// Verifies that a token that names no role is refused with a message that lists the roles.
    /// </summary>
    /// <param name="json">The JSON token.</param>
    [Theory]
    [InlineData("\"Server\"")]
    [InlineData("\"Workstation,Origin\"")]
    [InlineData("\"3\"")]
    [InlineData("42")]
    [InlineData("true")]
    public void Unknown_Role_Is_Refused(string json)
    {
        var read = () => JsonSerializer.Deserialize<NodeRole>(json);

        read.Should().Throw<JsonException>().WithMessage("*Workstation, Relay, Origin*");
    }

    /// <summary>
    /// Verifies that the capabilities exist for every role under its new name.
    /// </summary>
    [Fact]
    public void Every_Role_Has_Capabilities()
    {
        RoleCapabilities.For(NodeRole.Workstation).FetchesGit.Should().BeTrue();
        RoleCapabilities.For(NodeRole.Origin).ServesGit.Should().BeTrue();
        RoleCapabilities.For(NodeRole.Relay).ShowsSyncTab.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a configuration file of an earlier version is rewritten with the current role names, that the
    /// original is kept as a backup and that everything else in the file stays.
    /// </summary>
    [Fact]
    public void File_Of_An_Earlier_Version_Is_Rewritten_And_Backed_Up()
    {
        var path = Write("""
            {
              // written by an earlier version
              "syncBridge": {
                "NodeId": "source-1",
                "role": "Source",
                "WorkdirPath": "C:/work/app",
                "Chain": [
                  { "Name": "relay", "PeerNodeId": "mediator-1", "PeerRole": "Mediator", "Host": "10.0.0.1" },
                  { "Name": "dest", "PeerNodeId": "dest-1", "peerRole": "Dest", "Host": "10.0.0.2", },
                ]
              }
            }
            """);
        var original = File.ReadAllText(path);

        ConfigurationFileMigrator.Migrate(path).Should().BeTrue();

        File.ReadAllText(path + ConfigurationFileMigrator.BACKUP_SUFFIX).Should().Be(original);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var section = document.RootElement.GetProperty("syncBridge");
        section.GetProperty("role").GetString().Should().Be("Workstation");
        section.GetProperty("NodeId").GetString().Should().Be("source-1", "the identifier of a configured node is its own business");
        section.TryGetProperty("WorkdirPath", out _).Should().BeFalse("the single path of an earlier version becomes the first repository");
        var repository = section.GetProperty("Repositories").EnumerateArray().Should().ContainSingle().Subject;
        repository.GetProperty("Id").GetGuid().Should().Be(RepositoryEntry.DefaultId, "its data stays where the earlier version kept it");
        repository.GetProperty("Path").GetString().Should().Be("C:/work/app");
        repository.GetProperty("Name").GetString().Should().BeEmpty("a Workstation takes the name the Origin uses at their first contact");
        section.TryGetProperty("ActiveRepository", out _).Should().BeFalse();
        var chain = section.GetProperty("Chain");
        chain[0].GetProperty("PeerRole").GetString().Should().Be("Relay");
        chain[0].GetProperty("Name").GetString().Should().Be("relay");
        chain[1].GetProperty("peerRole").GetString().Should().Be("Origin");
        chain[1].GetProperty("Name").GetString().Should().Be("dest", "link names are chosen by the user");
    }

    /// <summary>
    /// Verifies that the migration changes nothing the second time and never replaces the first backup.
    /// </summary>
    [Fact]
    public void Migration_Is_Idempotent()
    {
        var path = Write("""{ "SyncBridge": { "Role": "Dest", "RepoPath": "/srv/repo" } }""");
        var original = File.ReadAllText(path);
        ConfigurationFileMigrator.Migrate(path).Should().BeTrue();
        var migrated = File.ReadAllText(path);
        var written = File.GetLastWriteTimeUtc(path);

        ConfigurationFileMigrator.Migrate(path).Should().BeFalse();

        File.ReadAllText(path).Should().Be(migrated);
        File.GetLastWriteTimeUtc(path).Should().Be(written);
        File.ReadAllText(path + ConfigurationFileMigrator.BACKUP_SUFFIX).Should().Be(original);
    }

    /// <summary>
    /// Verifies that a file of the current version is not touched and gets no backup.
    /// </summary>
    [Fact]
    public void Current_File_Is_Left_Alone()
    {
        var path = Write("""{ "SyncBridge": { "Role": "Workstation", "Chain": [ { "PeerRole": "Origin" } ] } }""");
        var original = File.ReadAllText(path);

        ConfigurationFileMigrator.Migrate(path).Should().BeFalse();

        File.ReadAllText(path).Should().Be(original);
        File.Exists(path + ConfigurationFileMigrator.BACKUP_SUFFIX).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a missing file, a broken file and a file without the section are left to the regular loading.
    /// </summary>
    /// <param name="content">The file content, or <see langword="null"/> for no file.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "Other": { "Role": "Source" } }""")]
    [InlineData("""{ "SyncBridge": { "Role": 1, "Chain": "none" } }""")]
    public void Unreadable_Or_Foreign_File_Is_Left_Alone(string? content)
    {
        var path = content is null ? Path.Combine(_directory, "missing.json") : Write(content);

        ConfigurationFileMigrator.Migrate(path).Should().BeFalse();

        if (content is not null)
        {
            File.ReadAllText(path).Should().Be(content);
        }

        File.Exists(path + ConfigurationFileMigrator.BACKUP_SUFFIX).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the options of a migrated file validate as a node of the new role.
    /// </summary>
    [Fact]
    public void Migrated_File_Loads_As_Options()
    {
        var path = Write("""{ "SyncBridge": { "NodeId": "dest-1", "Role": "Dest", "RepoPath": "/srv/repo" } }""");
        ConfigurationFileMigrator.Migrate(path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var options = document.RootElement.GetProperty("SyncBridge").Deserialize<SyncBridgeOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        options.Role.Should().Be(NodeRole.Origin);
        options.Repositories.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new RepositoryOptions { Id = RepositoryEntry.DefaultId, Name = "repo", Path = "/srv/repo" },
            "an Origin names its repository after the folder");
        options.ActiveRepository.Should().Be("repo");
        var node = SyncBridgeOptionsValidator.ToNodeConfig(options);
        node.Role.Should().Be(NodeRole.Origin);
        node.RepoPath.Should().Be("/srv/repo");
    }

    /// <summary>
    /// Writes a configuration file.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>The path.</returns>
    private string Write(string content)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, content);
        return path;
    }
}
