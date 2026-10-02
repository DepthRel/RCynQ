using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Git.Manifest;
using RCynQ.Service.Hosting;
using RCynQ.Service.Tests.Support;

namespace RCynQ.Service.Tests;

/// <summary>
/// Tests for <see cref="ServiceHostBuilder.BindOptions"/>, in particular the merge behaviour of
/// <see cref="SyncBridgeOptions.ExcludedDirectories"/>.
/// </summary>
public class ServiceHostBuilderOptionsTests
{
    /// <summary>
    /// Verifies that a node keeps the built-in excluded directories when it configures none.
    /// </summary>
    [Fact]
    public void Excluded_Directories_Default_When_Not_Configured()
    {
        var options = ServiceHostBuilder.BindOptions(Configuration(new Dictionary<string, string?>
        {
            ["SyncBridge:NodeId"] = "workstation-1"
        }));

        options.ExcludedDirectories.Should().BeEquivalentTo(new SyncBridgeOptions().ExcludedDirectories);
    }

    /// <summary>
    /// Verifies that configured directory names are added to the built-in ones instead of replacing them, so a node that
    /// excludes one extra directory does not start scanning build output again.
    /// </summary>
    [Fact]
    public void Excluded_Directories_Are_Added_To_The_Defaults()
    {
        var options = ServiceHostBuilder.BindOptions(Configuration(new Dictionary<string, string?>
        {
            ["SyncBridge:NodeId"] = "workstation-1",
            ["SyncBridge:ExcludedDirectories:0"] = ".kilo"
        }));

        options.ExcludedDirectories.Should().Contain(".kilo");
        options.ExcludedDirectories.Should().Contain(new SyncBridgeOptions().ExcludedDirectories);
    }

    /// <summary>
    /// Verifies that a name that is already a default is not listed twice, whatever its case.
    /// </summary>
    [Fact]
    public void Excluded_Directories_Are_Not_Duplicated()
    {
        var options = ServiceHostBuilder.BindOptions(Configuration(new Dictionary<string, string?>
        {
            ["SyncBridge:NodeId"] = "workstation-1",
            ["SyncBridge:ExcludedDirectories:0"] = "bin",
            ["SyncBridge:ExcludedDirectories:1"] = "OBJ",
            ["SyncBridge:ExcludedDirectories:2"] = ".kilo",
            ["SyncBridge:ExcludedDirectories:3"] = ".kilo"
        }));

        options.ExcludedDirectories.Should().OnlyHaveUniqueItems();
        options.ExcludedDirectories.Should().ContainSingle(name => string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase));
        options.ExcludedDirectories.Should().ContainSingle(name => name == ".kilo");
    }

    /// <summary>
    /// Verifies that blank entries are dropped, because they would exclude nothing and only confuse diagnostics.
    /// </summary>
    [Fact]
    public void Excluded_Directories_Drop_Blank_Entries()
    {
        var options = ServiceHostBuilder.BindOptions(Configuration(new Dictionary<string, string?>
        {
            ["SyncBridge:NodeId"] = "workstation-1",
            ["SyncBridge:ExcludedDirectories:0"] = "   ",
            ["SyncBridge:ExcludedDirectories:1"] = "  .kilo  "
        }));

        options.ExcludedDirectories.Should().NotContain(string.Empty);
        options.ExcludedDirectories.Should().Contain(".kilo");
    }

    /// <summary>
    /// Verifies that the snapshot filter of a running service excludes both the configured directory and the built-in
    /// ones, which is what actually keeps build output out of the working tree scan.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Snapshot_Filter_Of_The_Service_Excludes_Configured_And_Default_Directories()
    {
        await using var host = await ServiceTestHost.StartAsync(
            NodeRole.Workstation,
            extraSettings: new Dictionary<string, string?> { ["SyncBridge:ExcludedDirectories:0"] = ".kilo" });

        var filter = host.Services.GetRequiredService<SnapshotFilter>();

        filter.IsDirectoryExcluded(".kilo").Should().BeTrue("the node configured this one");
        filter.IsDirectoryExcluded("obj").Should().BeTrue("configuring one directory must not drop the built-in ones");
        filter.IsDirectoryExcluded("node_modules").Should().BeTrue();
        filter.IsDirectoryExcluded("src").Should().BeFalse();
    }

    /// <summary>
    /// Builds a configuration from in-memory values.
    /// </summary>
    /// <param name="values">The configuration values.</param>
    /// <returns>The configuration.</returns>
    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
