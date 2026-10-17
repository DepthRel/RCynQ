using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests of the one-time enrollment keys: their <c>authorized_keys</c> line, its removal and the SSH command that logs in
/// with them.
/// </summary>
public sealed class EnrollmentKeyTests : IDisposable
{
    /// <summary>A valid Ed25519 public key line.</summary>
    private const string KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl";

    /// <summary>Another valid Ed25519 public key line.</summary>
    private const string OTHER_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIE2mfktackE6lnW8Hp/QiIkF7Dp5hFWxNzR5c9wXgKja";

    /// <summary>The ticket of the line.</summary>
    private static readonly Guid _ticket = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    /// <summary>The expiry in local time.</summary>
    private static readonly DateTime _expires = new(2026, 10, 17, 9, 30, 0, DateTimeKind.Local);

    /// <summary>A temporary directory for the authorized_keys file.</summary>
    private readonly string _directory = Directory.CreateTempSubdirectory("rcynq-enroll-").FullName;

    /// <summary>
    /// Verifies the line on Windows: the program path with spaces is quoted and every quote is escaped for the options
    /// of the line. The outer pair cmd.exe drops is added by the OpenSSH server itself (tried against a running sshd:
    /// with one more pair the command does not start).
    /// </summary>
    [Fact]
    public void Windows_Line_Quotes_For_Cmd()
    {
        var line = AuthorizedKeysManager.BuildEnrollmentLine(
            SshPublicKey.Parse(KEY),
            new[] { @"C:\Program Files\RCynQ\cli\rcynq.exe", "enroll", "--config", @"C:\Users\dev\RCynQ\syncbridge.json" },
            windowsShell: true,
            _expires,
            _ticket);

        line.Should().Be(
            "restrict,expiry-time=\"202610170930\",command=\"\\\"C:\\Program Files\\RCynQ\\cli\\rcynq.exe\\\" enroll --config C:\\Users\\dev\\RCynQ\\syncbridge.json\" " +
            KEY + " rcynq:enroll-0f8fad5bd9cb469fa16570867728950e");
        var entry = AuthorizedKeysManager.Parse(new[] { line }).Entries.Should().ContainSingle().Subject;
        entry.IsRestricted.Should().BeTrue();
        entry.Key.Comment.Should().StartWith(AuthorizedKeysManager.ENROLLMENT_COMMENT_PREFIX);
    }

    /// <summary>
    /// Verifies the line on Linux and macOS: every word in single quotes, no double quotes at all.
    /// </summary>
    [Fact]
    public void Posix_Line_Quotes_Every_Word()
    {
        var line = AuthorizedKeysManager.BuildEnrollmentLine(
            SshPublicKey.Parse(KEY), new[] { "/opt/rcynq/cli/rcynq", "enroll", "--config", "/home/dev/My Config/syncbridge.json" }, windowsShell: false, _expires, _ticket);

        line.Should().StartWith("restrict,expiry-time=\"202610170930\",command=\"'/opt/rcynq/cli/rcynq' 'enroll' '--config' '/home/dev/My Config/syncbridge.json'\" ssh-ed25519 ");
    }

    /// <summary>
    /// Verifies that words that could break out of the quoting are refused.
    /// </summary>
    /// <param name="word">The unsafe word.</param>
    /// <param name="windowsShell">Whether the shell is cmd.exe.</param>
    [Theory]
    [InlineData("C:\\x\"&calc", true)]
    [InlineData("C:\\%PATH%\\rcynq.exe", true)]
    [InlineData("/opt/it's/rcynq", false)]
    [InlineData("/opt/rcynq\ncalc", false)]
    public void Unsafe_Words_Are_Refused(string word, bool windowsShell)
    {
        var build = () => AuthorizedKeysManager.BuildEnrollmentLine(SshPublicKey.Parse(KEY), new[] { word, "enroll" }, windowsShell, _expires, _ticket);

        build.Should().Throw<ArgumentException>();
        var check = () => AuthorizedKeysManager.EnsureEnrollmentCommandSafe(new[] { word, "enroll" }, windowsShell);
        check.Should().Throw<ArgumentException>("the check before a ticket is issued refuses the same words");
    }

    /// <summary>
    /// Verifies that on Windows a word with a character cmd.exe acts on is put in quotes even without a space, so the
    /// character stays part of the path.
    /// </summary>
    [Fact]
    public void Windows_Line_Quotes_Cmd_Special_Characters()
    {
        var line = AuthorizedKeysManager.BuildEnrollmentLine(
            SshPublicKey.Parse(KEY), new[] { @"D:\R&D\rcynq.exe", "enroll" }, windowsShell: true, _expires, _ticket);

        line.Should().StartWith("restrict,expiry-time=\"202610170930\",command=\"\\\"D:\\R&D\\rcynq.exe\\\" enroll\" ");
    }

    /// <summary>
    /// Verifies that only the enrollment lines of closed tickets are removed; other lines and open tickets stay.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Stale_Enrollment_Lines_Are_Removed()
    {
        var path = Path.Combine(_directory, "authorized_keys");
        var manager = new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = path });
        var open = AuthorizedKeysManager.BuildEnrollmentLine(SshPublicKey.Parse(KEY), new[] { "/opt/rcynq", "enroll" }, false, _expires, Guid.NewGuid());
        var closed = AuthorizedKeysManager.BuildEnrollmentLine(SshPublicKey.Parse(OTHER_KEY), new[] { "/opt/rcynq", "enroll" }, false, _expires, Guid.NewGuid());
        await File.WriteAllLinesAsync(path, new[] { "# mine", "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIE2mfktackE6lnW8Hp/QiIkF7Dp5hFWxNzR5c9wXgKja dev@laptop", open, closed });

        var removed = await manager.RemoveEnrollmentLinesAsync(new HashSet<string> { SshPublicKey.Parse(KEY).Blob });

        removed.Should().Be(1);
        (await File.ReadAllLinesAsync(path)).Should().Equal("# mine", "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIE2mfktackE6lnW8Hp/QiIkF7Dp5hFWxNzR5c9wXgKja dev@laptop", open);
        (await manager.RemoveEnrollmentLinesAsync(new HashSet<string> { SshPublicKey.Parse(KEY).Blob })).Should().Be(0);
    }

    /// <summary>
    /// Verifies that the command with a key names that key first, so the target sees it before the key of the node.
    /// </summary>
    [Fact]
    public void Command_With_A_Key_Offers_That_Key_First()
    {
        var builder = new SshCommandBuilder(new SshClientOptions { IdentityFile = "node_ed25519" });

        var args = builder.BuildExec(new SshEndpoint("rcynq", "relay.example"), Array.Empty<SshEndpoint>(), "/tmp/enroll_key", "cmVxdWVzdA");

        args.Take(4).Should().Equal("-i", "/tmp/enroll_key", "-o", "IdentitiesOnly=yes");
        args[^1].Should().Be("cmVxdWVzdA");
        var invalid = () => builder.BuildExec(new SshEndpoint("rcynq", "relay.example"), Array.Empty<SshEndpoint>(), "key\nfile", "x");
        invalid.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
