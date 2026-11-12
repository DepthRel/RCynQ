using FluentAssertions;
using RCynQ.Core.Config;

namespace RCynQ.Core.Tests;

/// <summary>
/// Tests of <see cref="NodeDataEraser"/>: a reset removes the configuration and the data of the node, and nothing else.
/// </summary>
public sealed class NodeDataEraserTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-node-reset", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The eraser under test.</summary>
    private readonly NodeDataEraser _eraser = new();

    /// <summary>
    /// Verifies that the configuration, the data of the node and its data directory go, while the settings of the user
    /// interface and the files of other programs in the same folders stay.
    /// </summary>
    [Fact]
    public void Reset_Removes_The_Node_And_Keeps_Everything_Else()
    {
        var config = WriteConfig(out var options);
        var paths = AppDataPaths.FromRoot(Path.Combine(_root, "data"));
        FillData(paths);
        var uiSettings = Path.Combine(_root, "ui-settings.json");
        File.WriteAllText(uiSettings, "{}");

        var plan = _eraser.Plan(options, config, portableRoot: null);

        plan.NodeId.Should().Be("node-1");
        plan.ConfigPath.Should().Be(config);
        plan.DataRoot.Should().Be(paths.Root);
        plan.HasData.Should().BeTrue();
        plan.Entries.Should().BeEquivalentTo(new[]
        {
            paths.DatabasePath, paths.DatabasePath + "-wal", paths.KeysDirectory, paths.PatchesDirectory, paths.SnapshotsDirectory,
            paths.LogsDirectory, paths.TempDirectory, Path.Combine(paths.Root, InstallationStamp.FILE_NAME), paths.WorkingTreeLockPath,
        });

        var report = _eraser.Erase(plan);

        report.IsComplete.Should().BeTrue();
        report.Removed.Should().Be(plan.Entries.Count + 1, "every entry and the configuration file");
        File.Exists(config).Should().BeFalse();
        Directory.Exists(paths.Root).Should().BeFalse("nothing else was in the data directory");
        File.Exists(uiSettings).Should().BeTrue("the settings of the user interface belong to the application, not to the node");
    }

    /// <summary>
    /// Verifies that a file of another program in the data directory, and the directory with it, stay.
    /// </summary>
    [Fact]
    public void Foreign_Files_In_The_Data_Directory_Stay()
    {
        var config = WriteConfig(out var options);
        var paths = AppDataPaths.FromRoot(Path.Combine(_root, "data"));
        FillData(paths);
        var foreign = Path.Combine(paths.Root, "notes.txt");
        File.WriteAllText(foreign, "mine");

        var report = _eraser.Erase(_eraser.Plan(options, config, portableRoot: null));

        report.IsComplete.Should().BeTrue();
        File.Exists(foreign).Should().BeTrue();
        Directory.Exists(paths.KeysDirectory).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a folder that does not hold the data of a node is not touched, even when it has folders with the
    /// names RCynQ uses, while the configuration file still goes.
    /// </summary>
    [Fact]
    public void Folder_Without_Node_Data_Is_Not_Touched()
    {
        var config = WriteConfig(out var options);
        var logs = Directory.CreateDirectory(Path.Combine(_root, "data", "logs")).FullName;
        File.WriteAllText(Path.Combine(logs, "app.log"), "not ours");

        var plan = _eraser.Plan(options, config, portableRoot: null);
        var report = _eraser.Erase(plan);

        plan.HasData.Should().BeFalse();
        plan.Entries.Should().BeEmpty();
        report.IsComplete.Should().BeTrue();
        File.Exists(Path.Combine(logs, "app.log")).Should().BeTrue();
        File.Exists(config).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a repository of the node that lies inside the data directory, under a name RCynQ uses, is never
    /// removed.
    /// </summary>
    [Fact]
    public void Repository_Inside_The_Data_Directory_Is_Never_Removed()
    {
        var config = WriteConfig(out var options);
        var paths = AppDataPaths.FromRoot(Path.Combine(_root, "data"));
        FillData(paths);
        var repository = Directory.CreateDirectory(Path.Combine(paths.TempDirectory, "work")).FullName;
        File.WriteAllText(Path.Combine(repository, "Program.cs"), "code");
        options.Repositories.Add(new RepositoryOptions { Id = Guid.NewGuid(), Name = "work", Path = repository });

        var plan = _eraser.Plan(options, config, portableRoot: null);
        _eraser.Erase(plan);

        plan.Entries.Should().NotContain(paths.TempDirectory);
        File.Exists(Path.Combine(repository, "Program.cs")).Should().BeTrue();
        Directory.Exists(paths.KeysDirectory).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that read-only files, as Git keeps its objects, do not stop the reset.
    /// </summary>
    [Fact]
    public void Read_Only_Files_Are_Removed()
    {
        var config = WriteConfig(out var options);
        var paths = AppDataPaths.FromRoot(Path.Combine(_root, "data"));
        FillData(paths);
        var objects = Directory.CreateDirectory(Path.Combine(paths.SnapshotsDirectory, "base", ".git", "objects", "ab")).FullName;
        var file = Path.Combine(objects, "cdef");
        File.WriteAllText(file, "blob");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var report = _eraser.Erase(_eraser.Plan(options, config, portableRoot: null));

        report.IsComplete.Should().BeTrue();
        Directory.Exists(paths.SnapshotsDirectory).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the configuration stays when a part of the data could not be removed, so the reset can be run
    /// again. Windows only: elsewhere a file that is open can be removed.
    /// </summary>
    [Fact]
    public void Configuration_Stays_When_Data_Is_Left()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var config = WriteConfig(out var options);
        var paths = AppDataPaths.FromRoot(Path.Combine(_root, "data"));
        FillData(paths);

        NodeResetReport report;
        using (new FileStream(paths.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            report = _eraser.Erase(_eraser.Plan(options, config, portableRoot: null));
        }

        report.IsComplete.Should().BeFalse();
        report.Failed.Should().Equal(paths.DatabasePath);
        File.Exists(config).Should().BeTrue();
        Directory.Exists(paths.KeysDirectory).Should().BeFalse("the rest went");
    }

    /// <summary>
    /// Verifies that the data directory of a portable installation is the one the plan names.
    /// </summary>
    [Fact]
    public void Portable_Installation_Uses_Its_Data_Directory()
    {
        var config = WriteConfig(out var options);
        options.DataRoot = null;

        var plan = _eraser.Plan(options, config, portableRoot: _root);

        plan.DataRoot.Should().Be(Path.Combine(_root, ConfigLocations.PORTABLE_DATA_DIRECTORY));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Writes a configuration file whose data directory is <c>data</c> next to it.
    /// </summary>
    /// <param name="options">The options the file holds.</param>
    /// <returns>The path of the file.</returns>
    private string WriteConfig(out SyncBridgeOptions options)
    {
        options = new SyncBridgeOptions { NodeId = "node-1", DataRoot = "data" };
        var path = Path.Combine(_root, SyncBridgeOptions.DEFAULT_FILE_NAME);
        File.WriteAllText(path, "{ \"SyncBridge\": { \"NodeId\": \"node-1\", \"DataRoot\": \"data\" } }");
        return path;
    }

    /// <summary>
    /// Creates the data of a node: the database, keys, patches, a snapshot, logs, temporary files, the stamp and the lock.
    /// </summary>
    /// <param name="paths">The paths of the data.</param>
    private static void FillData(AppDataPaths paths)
    {
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.DatabasePath, "db");
        File.WriteAllText(paths.DatabasePath + "-wal", "wal");
        Directory.CreateDirectory(paths.SshKeysDirectory);
        File.WriteAllText(paths.ApiTokenPath, "token");
        Directory.CreateDirectory(Path.Combine(paths.PatchesDirectory, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(paths.SyncBaseDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
        Directory.CreateDirectory(paths.TempDirectory);
        File.WriteAllText(Path.Combine(paths.Root, InstallationStamp.FILE_NAME), "{}");
        File.WriteAllText(paths.WorkingTreeLockPath, string.Empty);
    }
}
