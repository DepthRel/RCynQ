using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;
using RCynQ.MergeTools.Enums;
using RCynQ.MergeTools.Tests.Fakes;

namespace RCynQ.MergeTools.Tests;

/// <summary>
/// Tests of starting a merge tool.
/// </summary>
public sealed class MergeToolLauncherTests
{
    /// <summary>The executable of the tool.</summary>
    private const string EXECUTABLE = @"C:\Tools\merge.exe";

    /// <summary>The files of the merge.</summary>
    private static readonly MergeToolFiles _files = new(@"C:\data\merges\s\0\a.local.cs", @"C:\data\merges\s\0\a.base.cs", @"C:\data\merges\s\0\a.incoming.cs", @"C:\data\merges\s\0\a.cs");

    /// <summary>The titles of the panes.</summary>
    private static readonly MergeToolLabels _labels = new("Local", "Base", "Incoming", "Result");

    /// <summary>Starts the tool.</summary>
    private readonly IMergeToolProcessStarter _processes = Substitute.For<IMergeToolProcessStarter>();

    /// <summary>
    /// Verifies that the tool is started directly with its filled-in arguments, in the folder of the result.
    /// </summary>
    [Fact]
    public void The_Tool_Starts_With_Its_Arguments_In_The_Folder_Of_The_Result()
    {
        var running = Substitute.For<IRunningProcess>();
        ProcessSpec? started = null;
        _processes.Start(Arg.Do<ProcessSpec>(spec => started = spec)).Returns(running);
        var tool = new MergeTool("custom", "merge", EXECUTABLE, new[] { "/out:{result}", "{local}", "{incoming}" }, false);

        var process = Launcher(new FakeMergeToolEnvironment(MergeToolPlatform.Windows, @"C:\Users\me").WithFiles(EXECUTABLE)).Start(tool, _files, _labels);

        process.Should().BeSameAs(running);
        started!.FileName.Should().Be(EXECUTABLE);
        started.Arguments.Should().Equal(@"/out:C:\data\merges\s\0\a.cs", _files.Local, _files.Incoming);
        started.WorkingDirectory.Should().Be(@"C:\data\merges\s\0");
    }

    /// <summary>
    /// Verifies that a tool whose executable is gone is reported by name and path, without starting anything.
    /// </summary>
    [Fact]
    public void A_Missing_Tool_Is_Reported()
    {
        var tool = new MergeTool("kdiff3", "KDiff3", EXECUTABLE, new[] { "{local}", "{incoming}", "{result}" }, true);

        var start = () => Launcher(new FakeMergeToolEnvironment(MergeToolPlatform.Windows, @"C:\Users\me")).Start(tool, _files, _labels);

        start.Should().Throw<SyncBridgeException>().Which.Message.Should().Contain("KDiff3").And.Contain(EXECUTABLE);
        _processes.DidNotReceiveWithAnyArgs().Start(default!);
    }

    /// <summary>
    /// Creates the launcher on a machine.
    /// </summary>
    /// <param name="machine">The machine.</param>
    /// <returns>The launcher.</returns>
    private MergeToolLauncher Launcher(IMergeToolEnvironment machine) => new(TestTexts.English, _processes, machine);
}
