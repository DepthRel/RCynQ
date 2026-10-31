using CliFx.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Tests.Support;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Sync.Services;

namespace RCynQ.Cli.Tests;

/// <summary>
/// Tests of <c>rcynq patch save</c>, <c>patch files</c> and <c>patch apply</c>.
/// </summary>
public sealed class PatchFileCommandTests : IDisposable
{
    /// <summary>The bytes of the patch of the tests.</summary>
    private static readonly byte[] _patch = "diff --git a/a.txt b/a.txt\n"u8.ToArray();

    /// <summary>A temporary directory.</summary>
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "rcynq-cli-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The patch service substitute.</summary>
    private readonly IPatchService _patches = Substitute.For<IPatchService>();

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchFileCommandTests"/> class.
    /// </summary>
    public PatchFileCommandTests()
    {
        Directory.CreateDirectory(_temp);
        _patches.BuildPatchFileAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>()).Returns(_patch);
    }

    /// <summary>The path of the patch file of a test.</summary>
    private string PatchPath => Path.Combine(_temp, "changes.patch");

    /// <summary>
    /// Verifies that <c>patch save</c> writes the file and leaves the changes where they are.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Save_Writes_The_File_And_Keeps_The_Changes()
    {
        var (code, output, _) = await RunAsync("patch", "save", PatchPath);

        code.Should().Be(0);
        output.Should().Contain(TestTexts.English.Format("SavePatch.Saved", PatchPath));
        (await File.ReadAllBytesAsync(PatchPath)).Should().Equal(_patch);
        await _patches.DidNotReceiveWithAnyArgs().ApplyPatchFileAsync(default!, default, default, default);
    }

    /// <summary>
    /// Verifies that named files narrow the patch.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Save_Takes_Only_The_Named_Files()
    {
        await RunAsync("patch", "save", PatchPath, "--path", "a.txt", "--path", "b.txt");

        await _patches.Received(1).BuildPatchFileAsync(
            Arg.Is<IReadOnlyCollection<string>?>(paths => paths != null && paths.SequenceEqual(new[] { "a.txt", "b.txt" })),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that <c>--discard</c> takes the changes out only after the file is written.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Discard_Takes_The_Changes_Out_After_The_File_Is_Written()
    {
        _patches.ApplyPatchFileAsync(_patch, null, true, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            File.Exists(PatchPath).Should().BeTrue("the changes leave the working tree only once they are safe in the file");
            return new PatchFileApplyResult(true, "taken out", "output");
        });

        var (code, output, _) = await RunAsync("patch", "save", PatchPath, "--discard");

        code.Should().Be(0);
        output.Should().Contain(TestTexts.English.Format("SavePatch.SavedAndRemoved", PatchPath));
    }

    /// <summary>
    /// Verifies that changes that could not be taken out are reported with exit code 1, and the file stays.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Failed_Discard_Is_Reported()
    {
        _patches.ApplyPatchFileAsync(_patch, null, true, Arg.Any<CancellationToken>()).Returns(new PatchFileApplyResult(false, "does not fit", "git output"));

        var (code, _, error) = await RunAsync("patch", "save", PatchPath, "--discard");

        code.Should().Be(1);
        error.Should().Contain(TestTexts.English.Format("SavePatch.SavedNotRemoved", PatchPath, "does not fit"));
        File.Exists(PatchPath).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an existing file is replaced only with <c>--force</c>.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Existing_File_Needs_Force()
    {
        await File.WriteAllTextAsync(PatchPath, "old");

        var (code, _, error) = await RunAsync("patch", "save", PatchPath);

        code.Should().Be(1);
        error.Should().Contain(TestTexts.English.Format("PatchFile.FileExists", PatchPath));
        (await File.ReadAllTextAsync(PatchPath)).Should().Be("old");

        (await RunAsync("patch", "save", PatchPath, "--force")).Code.Should().Be(0);
        (await File.ReadAllBytesAsync(PatchPath)).Should().Equal(_patch);
    }

    /// <summary>
    /// Verifies that a node without changes writes no file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Nothing_To_Save_Writes_No_File()
    {
        _patches.BuildPatchFileAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<byte>());

        var (code, output, _) = await RunAsync("patch", "save", PatchPath);

        code.Should().Be(0);
        output.Should().Contain(TestTexts.English.Get("SavePatch.Nothing"));
        File.Exists(PatchPath).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <c>patch files</c> lists what a patch changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Files_Lists_The_Files_Of_A_Patch()
    {
        await File.WriteAllBytesAsync(PatchPath, _patch);
        _patches.ReadPatchFileAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ChangedFile(ChangeKind.Modified, "src/a.cs", false),
            new ChangedFile(ChangeKind.Added, "src/new.cs", false)
        });

        var (code, output, _) = await RunAsync("patch", "files", PatchPath);

        code.Should().Be(0);
        output.Should().Contain("M src/a.cs").And.Contain("A src/new.cs");
    }

    /// <summary>
    /// Verifies that <c>patch apply</c> applies the named files and prints the outcome with the output of Git.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Apply_Uses_The_Named_Files()
    {
        await File.WriteAllBytesAsync(PatchPath, _patch);
        _patches.ApplyPatchFileAsync(Arg.Any<byte[]>(), Arg.Any<IReadOnlyCollection<string>?>(), false, Arg.Any<CancellationToken>())
            .Returns(new PatchFileApplyResult(true, "applied", "Applied patch a.txt cleanly."));

        var (code, output, _) = await RunAsync("patch", "apply", PatchPath, "--path", "a.txt");

        code.Should().Be(0);
        output.Should().Contain("applied").And.Contain("Applied patch a.txt cleanly.");
        await _patches.Received(1).ApplyPatchFileAsync(
            Arg.Is<byte[]>(bytes => bytes.SequenceEqual(_patch)),
            Arg.Is<IReadOnlyCollection<string>?>(paths => paths != null && paths.Single() == "a.txt"),
            false,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a patch that did not apply ends with exit code 1 and the words of the program.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Patch_That_Did_Not_Apply_Is_Reported()
    {
        await File.WriteAllBytesAsync(PatchPath, _patch);
        _patches.ApplyPatchFileAsync(Arg.Any<byte[]>(), null, false, Arg.Any<CancellationToken>())
            .Returns(new PatchFileApplyResult(false, "does not fit", "error: a.txt: patch does not apply"));

        var (code, output, error) = await RunAsync("patch", "apply", PatchPath);

        code.Should().Be(1);
        error.Should().Contain("does not fit");
        output.Should().Contain("patch does not apply");
    }

    /// <summary>
    /// Verifies that a missing patch file is an error of the input, exit code 2.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Missing_Patch_File_Is_Invalid_Input()
    {
        (await RunAsync("patch", "apply", PatchPath)).Code.Should().Be(2);
    }

    /// <summary>
    /// Verifies that a failure of the service, such as a Relay asked for a patch, keeps its message.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Failure_Of_The_Node_Keeps_Its_Message()
    {
        _patches.BuildPatchFileAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>()).Returns<byte[]>(_ => throw new SyncBridgeException("no working tree"));

        var (code, _, error) = await RunAsync("patch", "save", PatchPath);

        code.Should().Be(1);
        error.Should().Contain("no working tree");
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Runs the CLI with an in-memory console.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The exit code, standard output and standard error.</returns>
    private async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        using var console = new FakeInMemoryConsole();
        var factory = new TestNodeContextFactory(services => services.AddSingleton(_patches));
        var code = await CliHost.CreateApplication(factory, TestTexts.English, console).RunAsync(args, new Dictionary<string, string>());
        return (code, console.ReadOutputString(), console.ReadErrorString());
    }
}
