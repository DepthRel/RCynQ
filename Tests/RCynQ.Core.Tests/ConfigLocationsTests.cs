using FluentAssertions;
using RCynQ.Core.Config;

namespace RCynQ.Core.Tests;

/// <summary>
/// Tests of <see cref="ConfigLocations"/>.
/// </summary>
public sealed class ConfigLocationsTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-config-locations", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies the precedence outside a portable installation: explicit path, environment, local file, per-user file.
    /// </summary>
    [Fact]
    public void Resolves_In_Order()
    {
        var local = Path.Combine(_root, SyncBridgeOptions.DefaultFileName);
        var user = Path.Combine(_root, "user", SyncBridgeOptions.DefaultFileName);
        var fromEnvironment = Path.Combine(_root, "env.json");
        Func<string, string?> noEnvironment = _ => null;
        Func<string?> notPortable = () => null;

        ConfigLocations.Resolve(null, noEnvironment, _root, user, notPortable).Should().Be(user, "without ./syncbridge.json the per-user file is used");

        File.WriteAllText(local, "{}");
        ConfigLocations.Resolve(null, noEnvironment, _root, user, notPortable).Should().Be(local);
        ConfigLocations.Resolve(null, name => name == ConfigLocations.EnvironmentVariable ? fromEnvironment : null, _root, user, notPortable).Should().Be(fromEnvironment);
        ConfigLocations.Resolve("explicit.json", _ => fromEnvironment, _root, user, notPortable).Should().Be(Path.GetFullPath("explicit.json"));
    }

    /// <summary>
    /// Verifies that a portable installation is detected from an application directory and keeps everything inside it.
    /// </summary>
    [Fact]
    public void Portable_Installation_Keeps_Config_And_Data_Inside()
    {
        var install = Directory.CreateDirectory(Path.Combine(_root, "RCynQ")).FullName;
        var ui = Directory.CreateDirectory(Path.Combine(install, "ui")).FullName;
        File.WriteAllText(Path.Combine(install, ConfigLocations.PortableMarkerFileName), "portable");
        File.WriteAllText(Path.Combine(ui, SyncBridgeOptions.DefaultFileName), "{}");

        ConfigLocations.FindPortableRoot(ui + Path.DirectorySeparatorChar).Should().Be(install);
        ConfigLocations.FindPortableRoot(install).Should().Be(install);
        ConfigLocations.FindPortableRoot(_root).Should().BeNull();

        var config = ConfigLocations.Resolve(null, _ => null, ui, Path.Combine(_root, "user.json"), () => install);
        config.Should().Be(Path.Combine(install, "config", SyncBridgeOptions.DefaultFileName), "the portable configuration wins over ./syncbridge.json and the user profile");

        var options = new SyncBridgeOptions { NodeId = "node-1" };
        ConfigLocations.ApplyStorageDefaults(options, config, install);
        options.DataRoot.Should().Be(Path.Combine(install, "data"));
        options.Ssh.KnownHostsFile.Should().Be(Path.Combine(install, "data", "keys", "known_hosts"));
    }

    /// <summary>
    /// Verifies that a relative data root is resolved against the configuration file and an explicit one is kept.
    /// </summary>
    [Fact]
    public void Data_Root_Is_Resolved_Against_Config()
    {
        var config = Path.Combine(_root, "node", SyncBridgeOptions.DefaultFileName);

        ConfigLocations.ResolveDataRoot(new SyncBridgeOptions { DataRoot = "data" }, config, null).Should().Be(Path.Combine(_root, "node", "data"));
        ConfigLocations.ResolveDataRoot(new SyncBridgeOptions { DataRoot = _root }, config, "/portable").Should().Be(_root);
        ConfigLocations.ResolveDataRoot(new SyncBridgeOptions { NodeId = "node-1" }, config, null).Should().EndWith(Path.Combine("RCynQ", "node-1"));

        var options = new SyncBridgeOptions { DataRoot = "data", Ssh = { KnownHostsFile = "/custom/known_hosts" } };
        ConfigLocations.ApplyStorageDefaults(options, config, null);
        options.Ssh.KnownHostsFile.Should().Be("/custom/known_hosts");
    }

    /// <summary>
    /// Verifies that the per-user file lives in an RCynQ folder.
    /// </summary>
    [Fact]
    public void User_Config_Is_In_RCynQ_Folder()
    {
        ConfigLocations.UserConfigPath.Should().EndWith(Path.Combine("RCynQ", SyncBridgeOptions.DefaultFileName));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
