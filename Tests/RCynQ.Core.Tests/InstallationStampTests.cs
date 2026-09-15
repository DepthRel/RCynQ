using FluentAssertions;
using RCynQ.Core.Config;
using Xunit;

namespace RCynQ.Core.Tests;

/// <summary>
/// Tests of <see cref="InstallationStamp"/>.
/// </summary>
public sealed class InstallationStampTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-stamp", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that the first computer is recorded and a different computer is reported until confirmed.
    /// </summary>
    [Fact]
    public void Records_Computer_And_Detects_Copy()
    {
        InstallationStamp.Check(_root, "PC-1").Should().Be(new InstallationCheck(false, "PC-1"));
        File.Exists(Path.Combine(_root, InstallationStamp.FileName)).Should().BeTrue();
        InstallationStamp.Check(_root, "pc-1").CopiedFromAnotherMachine.Should().BeFalse("computer names are case-insensitive");

        InstallationStamp.Check(_root, "PC-2").Should().Be(new InstallationCheck(true, "PC-1"));

        InstallationStamp.Write(_root, "PC-2");
        InstallationStamp.Check(_root, "PC-2").CopiedFromAnotherMachine.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a missing data directory is not created and a damaged stamp is ignored.
    /// </summary>
    [Fact]
    public void Missing_Or_Damaged_Stamp_Is_Not_A_Copy()
    {
        var missing = Path.Combine(_root, "not-created");
        InstallationStamp.Check(missing, "PC-1").CopiedFromAnotherMachine.Should().BeFalse();
        Directory.Exists(missing).Should().BeFalse();

        File.WriteAllText(Path.Combine(_root, InstallationStamp.FileName), "{ damaged");
        InstallationStamp.Check(_root, "PC-9").CopiedFromAnotherMachine.Should().BeFalse();
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
