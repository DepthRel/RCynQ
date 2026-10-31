using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;
using RCynQ.MergeTools.Enums;
using RCynQ.MergeTools.Tests.Fakes;

namespace RCynQ.MergeTools.Tests;

/// <summary>
/// Tests of the search for merge tools on a machine made of a list of files.
/// </summary>
public sealed class MergeToolLocatorTests
{
    /// <summary>The place of <c>vswhere</c> on Windows.</summary>
    private const string VSWHERE = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe";

    /// <summary>The place of VS Code installed for one user on Windows.</summary>
    private const string VSCODE = @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe";

    /// <summary>The place of KDiff3 on Windows.</summary>
    private const string KDIFF3 = @"C:\Program Files\KDiff3\kdiff3.exe";

    /// <summary>Starts <c>vswhere</c> and Git; nothing answers unless a test says so.</summary>
    private readonly IProcessRunner _processes = Substitute.For<IProcessRunner>();

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeToolLocatorTests"/> class.
    /// </summary>
    public MergeToolLocatorTests()
    {
        _processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(new ProcessResult(1, string.Empty, string.Empty));
    }

    /// <summary>
    /// Verifies that a tool is found in its usual place, with the variables of the user filled in.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Windows_Tool_Is_Found_In_Its_Usual_Place()
    {
        var scan = await Locator(Windows().WithFiles(VSCODE)).ScanAsync();

        var tool = scan.Tools.Should().ContainSingle().Subject;
        tool.ProfileId.Should().Be("vscode");
        tool.Name.Should().Be("Visual Studio Code");
        tool.ExecutablePath.Should().Be(VSCODE);
        scan.Suggested.Should().Be(tool);
    }

    /// <summary>
    /// Verifies that on Linux the commands are looked up in <c>PATH</c> and the places of Windows are not offered.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Linux_Commands_Are_Found_In_Path()
    {
        var machine = new FakeMergeToolEnvironment(MergeToolPlatform.Linux, "/home/me")
            .WithVariable("PATH", "/usr/local/bin:relative/bin:/usr/bin")
            .WithVariable("ProgramFiles", @"C:\Program Files")
            .WithFiles("/usr/bin/meld", "/usr/bin/kdiff3", "relative/bin/code", @"C:\Program Files\WinMerge\WinMergeU.exe");

        var scan = await Locator(machine).ScanAsync();

        scan.Tools.Select(tool => (tool.ProfileId, tool.ExecutablePath))
            .Should().BeEquivalentTo(new[] { ("kdiff3", "/usr/bin/kdiff3"), ("meld", "/usr/bin/meld") });
    }

    /// <summary>
    /// Verifies that the installations of JetBrains IDEs are named by the folder a <c>*</c> matched, one entry each.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task JetBrains_Installations_Are_Named_By_Their_Folder()
    {
        var machine = Windows().WithFiles(
            @"C:\Program Files\JetBrains\JetBrains Rider 2026.2\bin\rider64.exe",
            @"C:\Program Files\JetBrains\IntelliJ IDEA 2026.1\bin\idea64.exe",
            @"C:\Users\me\AppData\Local\Programs\IntelliJ IDEA Ultimate\bin\idea64.exe");

        var scan = await Locator(machine).ScanAsync();

        scan.Tools.Select(tool => tool.Name)
            .Should().BeEquivalentTo("JetBrains Rider 2026.2", "IntelliJ IDEA 2026.1", "IntelliJ IDEA Ultimate");
        scan.Tools.Single(tool => tool.ProfileId == "rider").Arguments.Should().StartWith("merge");
    }

    /// <summary>
    /// Verifies that the folder of a macOS application names the installation without its ending.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task MacOS_Applications_Are_Named_Without_The_Bundle_Ending()
    {
        var machine = new FakeMergeToolEnvironment(MergeToolPlatform.MacOS, "/Users/me")
            .WithFiles("/Applications/PyCharm.app/Contents/MacOS/pycharm");

        var scan = await Locator(machine).ScanAsync();

        scan.Tools.Should().ContainSingle(tool => tool.ProfileId == "pycharm" && tool.Name == "PyCharm");
    }

    /// <summary>
    /// Verifies that every installation of Visual Studio <c>vswhere</c> lists is offered under its own name when it has
    /// the merge tool.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Visual_Studio_Installations_Come_From_Vswhere()
    {
        const string JSON = """
            [
              { "installationPath": "C:\\Program Files\\Microsoft Visual Studio\\18\\Community", "displayName": "Visual Studio Community 2026" },
              { "installationPath": "C:\\BuildTools", "displayName": "Visual Studio Build Tools 2026" }
            ]
            """;
        _processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == VSWHERE), Arg.Any<CancellationToken>()).Returns(new ProcessResult(0, JSON, string.Empty));
        var merge = @"C:\Program Files\Microsoft Visual Studio\18\Community\" + VisualStudioInstallations.MERGE_TOOL_PATH;

        var scan = await Locator(Windows().WithFiles(VSWHERE, merge)).ScanAsync();

        var tool = scan.Tools.Should().ContainSingle().Subject;
        tool.ProfileId.Should().Be("vsdiffmerge");
        tool.Name.Should().Be("Visual Studio Community 2026");
        tool.ExecutablePath.Should().Be(merge);
        tool.TrustExitCode.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that output of <c>vswhere</c> that cannot be read finds nothing instead of failing.
    /// </summary>
    /// <param name="json">The output.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"installationPath\": \"C:\\\\VS\" }")]
    [InlineData("[ { \"displayName\": \"no path\" }, 42 ]")]
    public void Unreadable_Vswhere_Output_Finds_Nothing(string json)
    {
        VisualStudioInstallations.Parse(json).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the tool Git uses is offered first, and that the shell command of Git's configuration is never read.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Tool_Git_Uses_Is_Offered_First()
    {
        GitAnswers("merge.tool", "kdiff3");

        var scan = await Locator(Windows().WithFiles(VSCODE, KDIFF3)).ScanAsync();

        scan.Tools.Should().HaveCount(2);
        scan.Suggested!.ProfileId.Should().Be("kdiff3");
        await _processes.DidNotReceive().RunAsync(Arg.Is<ProcessSpec>(spec => spec.Arguments.Any(argument => argument.EndsWith(".cmd", StringComparison.Ordinal))), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the path Git's configuration gives the tool is used, written with forward slashes as Git for
    /// Windows writes it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Path_From_Git_Adds_The_Tool()
    {
        GitAnswers("merge.tool", "meld");
        GitAnswers("mergetool.meld.path", "D:/Tools/Meld/Meld.exe");

        var scan = await Locator(Windows().WithFiles(VSCODE, @"D:\Tools\Meld\Meld.exe")).ScanAsync();

        scan.Suggested!.ProfileId.Should().Be("meld");
        scan.Suggested.ExecutablePath.Should().Be(@"D:\Tools\Meld\Meld.exe");
        scan.Tools.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that a tool Git uses that RCynQ does not know, or a name Git could not use, changes nothing.
    /// </summary>
    /// <param name="name">The value of <c>merge.tool</c>.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("araxis")]
    [InlineData("kdiff3 --evil")]
    public async Task An_Unknown_Git_Tool_Changes_Nothing(string name)
    {
        GitAnswers("merge.tool", name);

        var scan = await Locator(Windows().WithFiles(VSCODE, KDIFF3)).ScanAsync();

        scan.Suggested!.ProfileId.Should().Be("vscode");
    }

    /// <summary>
    /// Verifies that a machine without the variables of the usual places, without Git and without tools finds nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Bare_Machine_Finds_Nothing()
    {
        _processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns<ProcessResult>(_ => throw new SyncBridgeException("git is missing"));

        var scan = await Locator(new FakeMergeToolEnvironment(MergeToolPlatform.Windows, @"C:\Users\me")).ScanAsync();

        scan.Tools.Should().BeEmpty();
        scan.Suggested.Should().BeNull();
    }

    /// <summary>
    /// Creates a Windows machine with the usual variables.
    /// </summary>
    /// <returns>The machine.</returns>
    private static FakeMergeToolEnvironment Windows()
        => new FakeMergeToolEnvironment(MergeToolPlatform.Windows, @"C:\Users\me")
            .WithVariable("LOCALAPPDATA", @"C:\Users\me\AppData\Local")
            .WithVariable("ProgramFiles", @"C:\Program Files")
            .WithVariable("ProgramFiles(x86)", @"C:\Program Files (x86)")
            .WithVariable("PATH", @"C:\Windows\System32");

    /// <summary>
    /// Makes Git answer a key of its global configuration.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    private void GitAnswers(string key, string value)
        => _processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "git" && spec.Arguments.Contains(key)), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, value + "\n", string.Empty));

    /// <summary>
    /// Creates the locator on a machine.
    /// </summary>
    /// <param name="machine">The machine.</param>
    /// <returns>The locator.</returns>
    private MergeToolLocator Locator(IMergeToolEnvironment machine) => new(machine, _processes);
}
