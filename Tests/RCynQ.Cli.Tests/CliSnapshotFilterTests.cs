using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Config;
using RCynQ.Git.DependencyInjection;
using RCynQ.Git.Manifest;
using RCynQ.Sync.Configuration;

namespace RCynQ.Cli.Tests;

/// <summary>
/// Tests that the CLI scans a working tree with the same excluded directories as the service.
/// </summary>
/// <remarks>
/// The service binds <c>syncbridge.json</c> through the configuration system, which appends the configured names to the
/// built-in ones, while the CLI deserializes the file, which replaces the list. Both work on the same data root, so a
/// filter that differed would make the CLI see build output as changes the service never offers.
/// </remarks>
public sealed class CliSnapshotFilterTests : IDisposable
{
    /// <summary>A temporary directory.</summary>
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "rcynq-cli-filter-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="CliSnapshotFilterTests"/> class.
    /// </summary>
    public CliSnapshotFilterTests()
    {
        Directory.CreateDirectory(_temp);
    }

    /// <summary>
    /// Verifies that a configuration file naming one extra directory keeps the built-in ones excluded for the CLI too.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Configured_Directories_Are_Added_To_The_Defaults_When_The_File_Is_Deserialized()
    {
        var path = Path.Combine(_temp, SyncBridgeOptions.DefaultFileName);
        await File.WriteAllTextAsync(path, """{ "SyncBridge": { "NodeId": "source-1", "ExcludedDirectories": [ ".kilo" ] } }""");

        var options = await ConfigurationFileEditor.LoadAsync(path);
        using var provider = new ServiceCollection().AddSingleton(options).AddRCynQGit().BuildServiceProvider();
        var filter = provider.GetRequiredService<SnapshotFilter>();

        filter.IsDirectoryExcluded(".kilo").Should().BeTrue("the node configured this one");
        foreach (var name in SyncBridgeOptions.DefaultExcludedDirectories)
        {
            filter.IsDirectoryExcluded(name).Should().BeTrue("configuring one directory must not drop the built-in '{0}'", name);
        }

        filter.IsDirectoryExcluded("src").Should().BeFalse();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }
}
