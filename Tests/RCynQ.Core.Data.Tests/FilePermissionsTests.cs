using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using RCynQ.Core.Data.Storage;

namespace RCynQ.Core.Data.Tests;

/// <summary>
/// Tests of <see cref="FilePermissions"/>.
/// </summary>
public sealed class FilePermissionsTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-permissions", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that a restricted directory and the files created in it are accessible only to the owner.
    /// </summary>
    [Fact]
    public void Restricted_Directory_Is_Owner_Only()
    {
        var keys = Directory.CreateDirectory(Path.Combine(_root, "keys")).FullName;
        FilePermissions.RestrictDirectoryToOwner(keys);
        var file = Path.Combine(keys, "secret.key");
        File.WriteAllText(file, "secret");
        FilePermissions.RestrictFileToOwner(file);

        File.ReadAllText(file).Should().Be("secret");
        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User!;
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            foreach (var security in new FileSystemSecurity[] { new DirectoryInfo(keys).GetAccessControl(), new FileInfo(file).GetAccessControl() })
            {
                security.AreAccessRulesProtected.Should().BeTrue();
                var identities = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Select(rule => rule.IdentityReference).Distinct().ToArray();
                identities.Should().BeEquivalentTo(new IdentityReference[] { user, system });
            }
        }
        else
        {
            File.GetUnixFileMode(keys).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.GetUnixFileMode(file).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }
}
