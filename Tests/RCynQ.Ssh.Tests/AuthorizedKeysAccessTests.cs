using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests of the automatic neighbour access in <c>authorized_keys</c>.
/// </summary>
public sealed class AuthorizedKeysAccessTests : IDisposable
{
    /// <summary>A public key.</summary>
    private const string KeyLine = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl dev@pc";

    /// <summary>Another public key.</summary>
    private const string OtherKeyLine = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPBzgtLuBSnvmhyPyE/aR3fT7G5Ynge9cP0KZC9vUVUb other";

    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-authkeys", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that a forward line allows only the listed targets and nothing else.
    /// </summary>
    [Fact]
    public void Forward_Line_Allows_Only_Listed_Targets()
    {
        var key = SshPublicKey.Parse(KeyLine);

        var line = AuthorizedKeysManager.BuildForwardLine(key, new[] { "192.168.10.20:22", "[fd00::5]:22" }, null, "rcynq:source-1");

        line.Should().Be($"restrict,port-forwarding,permitopen=\"192.168.10.20:22\",permitopen=\"[fd00::5]:22\",command=\"exit 1\",no-pty,no-agent-forwarding,no-x11-forwarding {key.Type} {key.Blob} rcynq:source-1");
        AuthorizedKeysManager.Parse(new[] { line }).Entries.Should().ContainSingle().Which.IsRestricted.Should().BeTrue();
        AuthorizedKeysManager.BuildForwardLine(key, new[] { "127.0.0.1:18443" }, "/srv/repo", "rcynq:source-1")
            .Should().Contain("permitopen=\"127.0.0.1:18443\",command=\"git-upload-pack '/srv/repo'\"");

        ((Action)(() => AuthorizedKeysManager.BuildForwardLine(key, Array.Empty<string>(), null, "rcynq:x"))).Should().Throw<ArgumentException>();
        ((Action)(() => AuthorizedKeysManager.BuildForwardLine(key, new[] { "-oProxyCommand=calc:22" }, null, "rcynq:x"))).Should().Throw<ArgumentException>();
        ((Action)(() => AuthorizedKeysManager.BuildForwardLine(key, new[] { "10.0.0.1:22" }, "/srv/my repo", "rcynq:x"))).Should().Throw<ArgumentException>();
        ((Action)(() => AuthorizedKeysManager.BuildForwardLine(key, new[] { "10.0.0.1:22" }, null, "rcynq:x\" evil"))).Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies that the line of the same key is replaced, other lines are kept and an unchanged file is not rewritten.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Upsert_Replaces_Same_Key_And_Keeps_Others()
    {
        var path = Path.Combine(_root, ".ssh", "authorized_keys");
        var key = SshPublicKey.Parse(KeyLine);
        var oldLine = AuthorizedKeysManager.BuildForwardLine(key, new[] { "10.0.0.1:22" }, null, "rcynq:source-1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, new[] { "# my keys", OtherKeyLine, oldLine });
        var manager = new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = path });
        var newLine = AuthorizedKeysManager.BuildForwardLine(key, new[] { "192.168.10.20:22" }, null, "rcynq:source-1");

        (await manager.UpsertAsync(new[] { newLine })).Should().Be(1);
        (await File.ReadAllLinesAsync(path)).Should().Equal("# my keys", OtherKeyLine, newLine);

        var written = File.GetLastWriteTimeUtc(path);
        (await manager.UpsertAsync(new[] { newLine })).Should().Be(0);
        File.GetLastWriteTimeUtc(path).Should().Be(written, "an unchanged file is not rewritten");
        Directory.GetFiles(Path.GetDirectoryName(path)!).Should().ContainSingle("no temporary file is left behind");

        var invalid = () => manager.UpsertAsync(new[] { "not a key" });
        await invalid.Should().ThrowAsync<FormatException>();
    }

    /// <summary>
    /// Verifies which file the SSH server reads on each platform.
    /// </summary>
    [Fact]
    public void Location_Follows_OpenSsh_Rules()
    {
        var options = new SshClientOptions();
        var askedForMembership = false;

        AuthorizedKeysLocation.Resolve(options, isWindows: true, () => true, @"C:\Users\dev", @"C:\ProgramData")
            .Should().Be(new AuthorizedKeysTarget(Path.Combine(@"C:\ProgramData", "ssh", "administrators_authorized_keys"), true));
        AuthorizedKeysLocation.Resolve(options, isWindows: true, () => false, @"C:\Users\dev", @"C:\ProgramData")
            .Should().Be(new AuthorizedKeysTarget(Path.Combine(@"C:\Users\dev", ".ssh", "authorized_keys"), false));
        AuthorizedKeysLocation.Resolve(options, isWindows: false, () => askedForMembership = true, "/home/dev", "/unused")
            .IsAdministratorsFile.Should().BeFalse();
        askedForMembership.Should().BeFalse("group membership only matters on Windows");

        var explicitPath = Path.Combine(_root, "custom_keys");
        AuthorizedKeysLocation.Resolve(new SshClientOptions { AuthorizedKeysPath = explicitPath }, isWindows: true, () => true, "u", "p")
            .Should().Be(new AuthorizedKeysTarget(explicitPath, false));
    }

    /// <summary>
    /// Verifies that the administrators file needs elevation and that group membership is read from <c>whoami</c>.
    /// </summary>
    [Fact]
    public void Administrators_File_Requires_Elevation()
    {
        var target = new AuthorizedKeysTarget(Path.Combine(_root, "administrators_authorized_keys"), true);

        new AuthorizedKeysManager(target, () => false).CanWrite.Should().BeFalse();
        new AuthorizedKeysManager(target, () => true).CanWrite.Should().BeTrue();
        new AuthorizedKeysManager(target with { IsAdministratorsFile = false }, () => false).CanWrite.Should().BeTrue();

        WindowsAccounts.ListsAdministrators("\"BUILTIN\\Administrators\",\"Alias\",\"S-1-5-32-544\",\"Group used for deny only\"").Should().BeTrue();
        WindowsAccounts.ListsAdministrators("\"BUILTIN\\Users\",\"Alias\",\"S-1-5-32-545\",\"Mandatory group\"").Should().BeFalse();
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
