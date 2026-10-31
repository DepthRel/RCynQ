using FluentAssertions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;

namespace RCynQ.MergeTools.Tests;

/// <summary>
/// Tests of starting the process of a merge tool, with a real process: the <c>dotnet</c> host the tests run with.
/// </summary>
public sealed class SystemMergeToolProcessStarterTests
{
    /// <summary>
    /// Verifies that the process starts, is waited for and tells its exit code.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Started_Process_Is_Waited_For()
    {
        using var process = new SystemMergeToolProcessStarter(TestTexts.English)
            .Start(new ProcessSpec("dotnet", new[] { "--version" }, Path.GetTempPath(), null, null));

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));

        process.HasExited.Should().BeTrue();
        process.ExitCode.Should().Be(0);
    }

    /// <summary>
    /// Verifies that an executable that cannot be started is reported with its name.
    /// </summary>
    [Fact]
    public void A_Missing_Executable_Is_Reported()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"rcynq-missing-{Guid.NewGuid():N}.exe");

        var start = () => new SystemMergeToolProcessStarter(TestTexts.English).Start(new ProcessSpec(missing, Array.Empty<string>(), null, null, null));

        start.Should().Throw<SyncBridgeException>().Which.Message.Should().Contain(missing);
    }
}
