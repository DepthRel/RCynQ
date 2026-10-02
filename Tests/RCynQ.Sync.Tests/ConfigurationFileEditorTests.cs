using System.Text.Json.Nodes;
using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Sync.Configuration;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="ConfigurationFileEditor"/> on files whose names are written in another case than the usual one.
/// </summary>
/// <remarks>
/// The service binds the file without regard to the case of the names, so the editor has to find them the same way.
/// </remarks>
public sealed class ConfigurationFileEditorTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rcynq-config-editor", Guid.NewGuid().ToString("N"));

    /// <summary>The configuration file.</summary>
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationFileEditorTests"/> class.
    /// </summary>
    public ConfigurationFileEditorTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "syncbridge.json");
    }

    /// <summary>
    /// Verifies that a file with a lower-case section and lower-case names is read.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Load_Finds_The_Section_Whatever_Its_Case()
    {
        await File.WriteAllTextAsync(_path, """{ "syncbridge": { "nodeId": "n1", "role": "Origin", "repoPath": "/srv/repo" } }""");

        var options = await ConfigurationFileEditor.LoadAsync(_path);

        options.NodeId.Should().Be("n1");
        options.Role.Should().Be(NodeRole.Origin);
        options.Repositories.Should().ContainSingle("the path of an earlier version becomes the first repository")
            .Which.Should().BeEquivalentTo(new RepositoryOptions { Id = RepositoryEntry.DefaultId, Name = "repo", Path = "/srv/repo" });
        options.ActiveRepository.Should().Be("repo");
    }

    /// <summary>
    /// Verifies that changing the role edits the properties the user wrote instead of adding a second section or a second
    /// property that differs only in case, which the service would refuse as a duplicate setting.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SetRole_Edits_The_Existing_Properties_Whatever_Their_Case()
    {
        await File.WriteAllTextAsync(_path, """{ "syncBridge": { "nodeId": "n1", "role": "Workstation", "workdirPath": "/work", "listenPort": 18443 } }""");

        await ConfigurationFileEditor.SetRoleAsync(_path, NodeRole.Origin, "/srv/repo", null);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(_path))!.AsObject();
        root.Select(pair => pair.Key).Should().Equal("syncBridge");
        var section = root["syncBridge"]!.AsObject();
        section.Select(pair => pair.Key.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        section["role"]!.GetValue<string>().Should().Be("Origin", "the property is edited where the user wrote it");
        section["listenPort"]!.GetValue<int>().Should().Be(18443, "other settings are kept");
        var loaded = await ConfigurationFileEditor.LoadAsync(_path);
        loaded.Role.Should().Be(NodeRole.Origin);
        loaded.Repositories.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new RepositoryOptions { Id = RepositoryEntry.DefaultId, Name = "repo", Path = "/srv/repo" },
            "the working copy the node had becomes the repository of the Origin, named after its folder");
        loaded.ActiveRepository.Should().Be("repo");
    }

    /// <summary>
    /// Verifies that a Relay loses its paths also when they are written in another case.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SetRole_To_Relay_Removes_Paths_Whatever_Their_Case()
    {
        await File.WriteAllTextAsync(_path, """{ "SyncBridge": { "NodeId": "n1", "Role": "Workstation", "workdirpath": "/work", "REPOPATH": "/srv/repo" } }""");

        await ConfigurationFileEditor.SetRoleAsync(_path, NodeRole.Relay, null, null);

        var loaded = await ConfigurationFileEditor.LoadAsync(_path);
        loaded.Role.Should().Be(NodeRole.Relay);
        loaded.Repositories.Should().BeEmpty();
        loaded.ActiveRepository.Should().BeNull();
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
