using FluentAssertions;
using NSubstitute;
using RCynQ.MergeTools;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Merging;
using RCynQ.UI.ViewModels;

namespace RCynQ.UI.Tests.Merging;

/// <summary>
/// Tests of the choice of the merge tool in the settings.
/// </summary>
public sealed class MergeToolSettingsViewModelTests : IDisposable
{
    /// <summary>VS Code found on the machine.</summary>
    private static readonly MergeTool _code = new("vscode", "Visual Studio Code", @"C:\Code\Code.exe", new[] { "{local}", "{incoming}", "{result}" }, false);

    /// <summary>KDiff3 found on the machine.</summary>
    private static readonly MergeTool _kdiff3 = new("kdiff3", "KDiff3", @"C:\KDiff3\kdiff3.exe", new[] { "{base}", "{local}", "{incoming}", "-o", "{result}" }, true);

    /// <summary>The merge tools and the choice.</summary>
    private readonly IMergeToolPreferences _preferences = Substitute.For<IMergeToolPreferences>();

    /// <summary>An executable that exists, for the tool the user describes.</summary>
    private readonly string _executable = Path.Combine(Path.GetTempPath(), $"rcynq-merge-{Guid.NewGuid():N}.exe");

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeToolSettingsViewModelTests"/> class: two tools are found and
    /// VS Code is the current one.
    /// </summary>
    public MergeToolSettingsViewModelTests()
    {
        File.WriteAllText(_executable, string.Empty);
        _preferences.GetToolsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new[] { _code, _kdiff3 });
        _preferences.GetDefaultAsync(Arg.Any<CancellationToken>()).Returns(_code);
    }

    /// <summary>
    /// Verifies that the found tools are listed with the entry of the tool the user describes, the current one selected,
    /// and that picking another one saves it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Picking_A_Found_Tool_Saves_It()
    {
        var settings = Settings();

        await settings.LoadAsync();

        settings.Options.Select(option => option.Name).Should().Equal("Visual Studio Code", "KDiff3", "Свой инструмент…");
        settings.Selected!.Tool.Should().Be(_code);
        settings.PathText.Should().Be(@"C:\Code\Code.exe");
        _preferences.DidNotReceiveWithAnyArgs().SetDefault(default!);

        settings.Selected = settings.Options[1];

        _preferences.Received(1).SetDefault(_kdiff3);
        settings.SavedText.Should().Contain("KDiff3");
    }

    /// <summary>
    /// Verifies that the tool the user describes is checked before it is saved, with a reason for each mistake.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Described_Tool_Is_Checked_Before_It_Is_Saved()
    {
        var settings = Settings();
        await settings.LoadAsync();
        settings.Selected = settings.Options.Last();
        settings.IsCustom.Should().BeTrue();

        settings.CustomPath = @"C:\nowhere\merge.exe";
        settings.SaveCustomCommand.Execute(null);
        settings.Error.Should().Be(TestTexts.Russian.Get("Settings.MergeTool.NoExecutable"));

        settings.CustomPath = _executable;
        settings.CustomArguments = "{local} {incoming}";
        settings.SaveCustomCommand.Execute(null);
        settings.Error.Should().Be(TestTexts.Russian.Get("Settings.MergeTool.MissingResult"));

        settings.CustomArguments = "{local} {incoming} {merged}";
        settings.SaveCustomCommand.Execute(null);
        settings.Error.Should().Be(TestTexts.Russian.Get("Settings.MergeTool.UnknownPlaceholder"));

        _preferences.DidNotReceiveWithAnyArgs().SetDefault(default!);
    }

    /// <summary>
    /// Verifies that a correct description of a tool is saved.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Correct_Described_Tool_Is_Saved()
    {
        var custom = new MergeTool(MergeTool.CUSTOM_ID, "merge", _executable, new[] { "{local}", "{incoming}", "{result}" }, false);
        _preferences.CreateCustom(_executable, "{local} {incoming} {result}").Returns(custom);
        var settings = Settings();
        await settings.LoadAsync();
        settings.Selected = settings.Options.Last();

        settings.CustomPath = _executable;
        settings.CustomArguments = "{local} {incoming} {result}";
        settings.SaveCustomCommand.Execute(null);

        settings.HasError.Should().BeFalse();
        _preferences.Received(1).SetDefault(custom);
    }

    /// <summary>
    /// Verifies that a described tool that is the current one is shown with its executable and arguments.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Current_Described_Tool_Is_Shown()
    {
        _preferences.GetDefaultAsync(Arg.Any<CancellationToken>())
            .Returns(new MergeTool(MergeTool.CUSTOM_ID, "merge", _executable, new[] { "/out:{result}", "{local}", "{incoming}" }, false));
        var settings = Settings();

        await settings.LoadAsync();

        settings.IsCustom.Should().BeTrue();
        settings.CustomPath.Should().Be(_executable);
        settings.CustomArguments.Should().Be("/out:{result} {local} {incoming}");
    }

    /// <summary>
    /// Verifies that searching again asks the machine again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Searching_Again_Asks_The_Machine_Again()
    {
        var settings = Settings();

        await settings.RescanCommand.ExecuteAsync(null);

        await _preferences.Received(1).GetToolsAsync(true, Arg.Any<CancellationToken>());
        settings.HasNoTools.Should().BeFalse();
    }

    /// <inheritdoc />
    public void Dispose() => File.Delete(_executable);

    /// <summary>
    /// Creates the choice of the settings.
    /// </summary>
    /// <returns>The choice.</returns>
    private MergeToolSettingsViewModel Settings() => new(TestTexts.Russian, _preferences, Substitute.For<IDialogService>());
}
