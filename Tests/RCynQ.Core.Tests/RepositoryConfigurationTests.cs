using System.Text.Json.Nodes;
using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Tests;

/// <summary>
/// Tests of the repositories of a node in its configuration: names, validation, the repository the node works with and
/// where the node keeps its data about each one.
/// </summary>
public sealed class RepositoryConfigurationTests
{
    /// <summary>
    /// Verifies which texts are repository names.
    /// </summary>
    /// <param name="name">The text.</param>
    /// <param name="valid">Whether it is a valid name.</param>
    [Theory]
    [InlineData("app", true)]
    [InlineData("My.Project_2-x", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("with space", false)]
    [InlineData("проект", false)]
    [InlineData("a;rm -rf", false)]
    public void Names_Are_Restricted_To_Safe_Characters(string? name, bool valid)
    {
        RepositoryName.IsValid(name).Should().Be(valid);
    }

    /// <summary>
    /// Verifies that a name is never longer than the limit.
    /// </summary>
    [Fact]
    public void Names_Have_A_Length_Limit()
    {
        RepositoryName.IsValid(new string('a', RepositoryName.MAX_LENGTH)).Should().BeTrue();
        RepositoryName.IsValid(new string('a', RepositoryName.MAX_LENGTH + 1)).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the suggested name is the folder name made valid.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="expected">The suggestion.</param>
    [Theory]
    [InlineData("/srv/repos/app", "app")]
    [InlineData("D:\\Repos\\My Project\\", "My-Project")]
    [InlineData("/srv/проект", "repository")]
    [InlineData("", "repository")]
    [InlineData(null, "repository")]
    [InlineData("/srv/.hidden.", "hidden")]
    public void Suggested_Name_Comes_From_The_Folder(string? path, string expected)
    {
        var name = RepositoryName.Suggest(path);

        name.Should().Be(expected);
        RepositoryName.IsValid(name).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an entry refuses a name that is not valid and a blank path, and compares names without regard to case.
    /// </summary>
    [Fact]
    public void Entry_Validates_Its_Name_And_Path()
    {
        var bad = () => new RepositoryEntry(Guid.NewGuid(), "a/b", "/srv/a");
        var blank = () => new RepositoryEntry(Guid.NewGuid(), "a", " ");
        var entry = new RepositoryEntry(Guid.NewGuid(), "App", "/srv/app");

        bad.Should().Throw<ArgumentException>();
        blank.Should().Throw<ArgumentException>();
        entry.IsNamed("app").Should().BeTrue();
        entry.IsNamed("other").Should().BeFalse();
        entry.IsNamed(null).Should().BeFalse();
        new RepositoryEntry(RepositoryEntry.DefaultId, string.Empty, "/work").IsBound.Should().BeFalse();
        new RepositoryEntry(RepositoryEntry.DefaultId, string.Empty, "/work").IsNamed(string.Empty).Should().BeFalse("a folder without a name is no repository anybody can ask for");
    }

    /// <summary>
    /// Verifies that the node works with the named repository, or with the first one when none is named.
    /// </summary>
    [Fact]
    public void Active_Repository_Is_The_Named_One_Or_The_First()
    {
        var options = Origin(("app", "/srv/app"), ("tools", "/srv/tools"));

        SyncBridgeOptionsValidator.ActiveRepositoryOf(options)!.Name.Should().Be("app");
        options.ActiveRepository = "TOOLS";
        SyncBridgeOptionsValidator.ActiveRepositoryOf(options)!.Name.Should().Be("tools");
        SyncBridgeOptionsValidator.ToNodeConfig(options).RepoPath.Should().Be("/srv/tools");
        SyncBridgeOptionsValidator.Validate(options, TestTexts.English).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the problems the validation reports.
    /// </summary>
    [Fact]
    public void Validation_Reports_Bad_Repositories()
    {
        Errors(Origin(("app", "/srv/app"), ("APP", "/srv/other"))).Should().ContainSingle().Which.Should().Contain("more than once");
        Errors(Origin(("a b", "/srv/app"))).Should().ContainSingle().Which.Should().Contain("not valid");
        Errors(Origin(("app", " "))).Should().Contain(error => error.Contains("no path"));
        Errors(Origin((string.Empty, "/srv/app"))).Should().ContainSingle().Which.Should().Contain("needs a name");
        Errors(Origin()).Should().ContainSingle().Which.Should().Contain("at least one repository");

        var unknown = Origin(("app", "/srv/app"));
        unknown.ActiveRepository = "gone";
        Errors(unknown).Should().ContainSingle().Which.Should().Contain("'gone'");

        var sameId = Origin(("app", "/srv/app"), ("tools", "/srv/tools"));
        sameId.Repositories[1].Id = sameId.Repositories[0].Id;
        Errors(sameId).Should().ContainSingle().Which.Should().Contain("identifier");

        var relay = new SyncBridgeOptions { NodeId = "relay-1", Role = NodeRole.Relay, Repositories = { new RepositoryOptions { Name = "app", Path = "/srv/app" } } };
        Errors(relay).Should().ContainSingle().Which.Should().Contain("Relay");
    }

    /// <summary>
    /// Verifies that a Workstation may have one folder without a name, and no more.
    /// </summary>
    [Fact]
    public void Workstation_May_Have_One_Folder_That_Is_Not_Bound_Yet()
    {
        var one = new SyncBridgeOptions { NodeId = "workstation-1", Role = NodeRole.Workstation, Repositories = { new RepositoryOptions { Path = "/work" } } };
        var two = new SyncBridgeOptions
        {
            NodeId = "workstation-1",
            Role = NodeRole.Workstation,
            Repositories = { new RepositoryOptions { Path = "/work" }, new RepositoryOptions { Id = Guid.NewGuid(), Path = "/other" } }
        };

        Errors(one).Should().BeEmpty();
        SyncBridgeOptionsValidator.ToNodeConfig(one).WorkdirPath.Should().Be("/work");
        Errors(two).Should().ContainSingle().Which.Should().Contain("needs a name");
    }

    /// <summary>
    /// Verifies that the repository a node started with keeps its data where an earlier version kept it, and every other
    /// repository gets a folder of its own.
    /// </summary>
    [Fact]
    public void Data_Of_Each_Repository_Has_Its_Own_Place()
    {
        var paths = AppDataPaths.FromRoot(Path.Combine(Path.GetTempPath(), "rcynq-data"));
        var id = Guid.NewGuid();

        paths.ForRepository(RepositoryEntry.DefaultId).Should().BeSameAs(paths);
        var other = paths.ForRepository(id);
        other.SyncBaseDirectory.Should().Be(Path.Combine(paths.SnapshotsDirectory, id.ToString("N"), "base"));
        other.SyncBaseMetadataPath.Should().NotBe(paths.SyncBaseMetadataPath);
        other.DatabasePath.Should().Be(paths.DatabasePath, "the database is one for the node");
        other.KeysDirectory.Should().Be(paths.KeysDirectory);
    }

    /// <summary>
    /// Verifies that the node described with a repository carries its path in the property of its role.
    /// </summary>
    [Fact]
    public void Node_Is_Described_With_The_Repository_It_Works_With()
    {
        var origin = SyncBridgeOptionsValidator.ToNodeConfig(Origin(("app", "/srv/app")));
        var other = new RepositoryEntry(Guid.NewGuid(), "tools", "/srv/tools");

        origin.WithRepository(other).RepoPath.Should().Be("/srv/tools");
        origin.WithRepository(null).Should().BeSameAs(origin);
        FixedRepository.FromNode(origin).Current!.Path.Should().Be("/srv/app");
        FixedRepository.FromNode(origin).Entries.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies the edits of the repositories in the JSON of the configuration file.
    /// </summary>
    [Fact]
    public void Editor_Sets_The_Path_Of_The_Repository_The_Node_Works_With()
    {
        var fresh = new JsonObject();
        RepositoryConfigurationEditor.Apply(fresh, NodeRole.Workstation, "/work/app");
        fresh["Repositories"]!.AsArray().Should().ContainSingle();
        fresh["Repositories"]![0]!["Name"]!.GetValue<string>().Should().BeEmpty("a Workstation learns the name from the Origin");
        fresh["Repositories"]![0]!["Id"]!.GetValue<string>().Should().Be(RepositoryEntry.DefaultId.ToString());

        RepositoryConfigurationEditor.Apply(fresh, NodeRole.Origin, "/srv/repos/app");
        fresh["Repositories"]!.AsArray().Should().ContainSingle("the path of the existing repository is replaced");
        fresh["Repositories"]![0]!["Path"]!.GetValue<string>().Should().Be("/srv/repos/app");
        fresh["Repositories"]![0]!["Name"]!.GetValue<string>().Should().Be("app", "an Origin names every repository");
        fresh["ActiveRepository"]!.GetValue<string>().Should().Be("app");

        var two = JsonNode.Parse("""{ "repositories": [ { "name": "a", "path": "/a" }, { "name": "b", "path": "/b" } ], "activeRepository": "B" }""")!.AsObject();
        RepositoryConfigurationEditor.Apply(two, NodeRole.Origin, "/moved");
        two["repositories"]![0]!["path"]!.GetValue<string>().Should().Be("/a");
        two["repositories"]![1]!["path"]!.GetValue<string>().Should().Be("/moved", "only the repository the node works with moves");
        two.Select(pair => pair.Key).Should().Equal("repositories", "activeRepository");

        RepositoryConfigurationEditor.Apply(two, NodeRole.Origin, null);
        two["repositories"]![1]!["path"]!.GetValue<string>().Should().Be("/moved", "no path keeps what is there");

        RepositoryConfigurationEditor.Apply(two, NodeRole.Relay, "/ignored");
        two.Should().BeEmpty("a Relay has no repositories");
    }

    /// <summary>
    /// Verifies that the path for the setup screens is read even from options that would not validate.
    /// </summary>
    [Fact]
    public void Active_Path_Is_Read_From_Unvalidated_Options()
    {
        RepositoryConfigurationEditor.ActivePath(null).Should().BeNull();
        RepositoryConfigurationEditor.ActivePath(new SyncBridgeOptions()).Should().BeNull();
        RepositoryConfigurationEditor.ActivePath(Origin(("a b", " /srv/app "))).Should().Be("/srv/app");
        var options = Origin(("app", "/srv/app"), ("tools", "/srv/tools"));
        options.ActiveRepository = "tools";
        RepositoryConfigurationEditor.ActivePath(options).Should().Be("/srv/tools");
    }

    /// <summary>
    /// Creates the options of an Origin with repositories.
    /// </summary>
    /// <param name="repositories">The names and paths.</param>
    /// <returns>The options.</returns>
    private static SyncBridgeOptions Origin(params (string Name, string Path)[] repositories)
    {
        var options = new SyncBridgeOptions { NodeId = "origin-1", Role = NodeRole.Origin };
        foreach (var (name, path) in repositories)
        {
            options.Repositories.Add(new RepositoryOptions { Id = options.Repositories.Count == 0 ? RepositoryEntry.DefaultId : Guid.NewGuid(), Name = name, Path = path });
        }

        return options;
    }

    /// <summary>
    /// Validates options.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The problems.</returns>
    private static IReadOnlyList<string> Errors(SyncBridgeOptions options)
        => SyncBridgeOptionsValidator.Validate(options, TestTexts.English);
}
