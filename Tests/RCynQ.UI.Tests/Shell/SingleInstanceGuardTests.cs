using FluentAssertions;
using RCynQ.UI.Services.Shell;

namespace RCynQ.UI.Tests.Shell;

/// <summary>
/// Tests of <see cref="SingleInstanceGuard"/>: one copy of the application per node, and a second start that shows the
/// window of the first. They use a real named mutex and a real named pipe under keys of their own.
/// </summary>
public sealed class SingleInstanceGuardTests
{
    /// <summary>How long a test waits for the other side.</summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Verifies that every spelling of the same configuration file gives the same key, that another file gives another
    /// key, and that the case of the letters counts only where the file system tells it apart.
    /// </summary>
    [Fact]
    public void Key_Follows_The_Configuration_File()
    {
        var root = Path.Combine(Path.GetTempPath(), "rcynq-guard-tests");
        var config = Path.Combine(root, "config", "syncbridge.json");
        var sameFile = Path.Combine(root, "config", "..", "config", "syncbridge.json");
        var upper = Path.Combine(root, "CONFIG", "syncbridge.json");

        var key = SingleInstanceGuard.KeyFor(config, ignoreCase: false);

        key.Should().StartWith("rcynq-ui-").And.HaveLength("rcynq-ui-".Length + 16);
        SingleInstanceGuard.KeyFor(sameFile, ignoreCase: false).Should().Be(key);
        SingleInstanceGuard.KeyFor(Path.Combine(root, "other", "syncbridge.json"), ignoreCase: false).Should().NotBe(key);
        SingleInstanceGuard.KeyFor(upper, ignoreCase: false).Should().NotBe(key);
        SingleInstanceGuard.KeyFor(upper, ignoreCase: true).Should().Be(SingleInstanceGuard.KeyFor(config, ignoreCase: true));
    }

    /// <summary>
    /// Verifies that a second copy cannot take the key while the first holds it, and can once the first is gone.
    /// </summary>
    [Fact]
    public void Second_Copy_Is_Refused_Until_The_First_Ends()
    {
        var key = UniqueKey();

        var first = SingleInstanceGuard.TryAcquire(key);
        first.Should().NotBeNull();
        SingleInstanceGuard.TryAcquire(key).Should().BeNull("the first copy holds the key");

        first!.Dispose();
        using var later = SingleInstanceGuard.TryAcquire(key);
        later.Should().NotBeNull("the first copy ended");
    }

    /// <summary>
    /// Verifies that a second copy reaches the first one, once for every start, and that nobody answers for a key no
    /// copy listens on.
    /// </summary>
    [Fact]
    public void Second_Copy_Asks_The_First_To_Show_Its_Window()
    {
        var key = UniqueKey();
        using var guard = SingleInstanceGuard.TryAcquire(key)!;
        var requests = new SemaphoreSlim(0);
        guard.Listen(() => requests.Release());

        SingleInstanceGuard.SignalExisting(key, _timeout).Should().BeTrue();
        requests.Wait(_timeout).Should().BeTrue("the first copy took the request");
        SingleInstanceGuard.SignalExisting(key, _timeout).Should().BeTrue();
        requests.Wait(_timeout).Should().BeTrue("every start is a request of its own");

        SingleInstanceGuard.SignalExisting(UniqueKey(), TimeSpan.FromMilliseconds(200)).Should().BeFalse();
    }

    /// <summary>
    /// Makes a key no other test and no running application uses.
    /// </summary>
    /// <returns>The key.</returns>
    private static string UniqueKey() => SingleInstanceGuard.KeyFor(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "syncbridge.json"));
}
