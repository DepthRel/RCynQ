using FluentAssertions;
using NSubstitute;
using RCynQ.MergeTools;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Merging;

namespace RCynQ.UI.Tests.Merging;

/// <summary>
/// Tests of the choice of the merge tool and how it is kept.
/// </summary>
public sealed class MergeToolPreferencesTests
{
    /// <summary>VS Code found on the machine.</summary>
    private static readonly MergeTool _code = new("vscode", "Visual Studio Code", @"C:\Code\Code.exe", new[] { "{local}", "{incoming}", "{result}" }, false);

    /// <summary>One installation of Rider.</summary>
    private static readonly MergeTool _rider = new("rider", "JetBrains Rider 2026.1", @"C:\JetBrains\Rider 2026.1\bin\rider64.exe", new[] { "merge", "{local}", "{incoming}", "{result}" }, false);

    /// <summary>Another installation of Rider.</summary>
    private static readonly MergeTool _riderNext = _rider with { Name = "JetBrains Rider 2026.2", ExecutablePath = @"C:\JetBrains\Rider 2026.2\bin\rider64.exe" };

    /// <summary>Finds the tools of the machine.</summary>
    private readonly IMergeToolLocator _locator = Substitute.For<IMergeToolLocator>();

    /// <summary>The settings of the interface, kept in a dictionary.</summary>
    private readonly Dictionary<string, string> _stored = new();

    /// <summary>The settings of the interface.</summary>
    private readonly IUiSettingsStore _settings = Substitute.For<IUiSettingsStore>();

    /// <summary>The machine.</summary>
    private readonly IMergeToolEnvironment _machine = Substitute.For<IMergeToolEnvironment>();

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeToolPreferencesTests"/> class: VS Code and two Rider installations
    /// are found, VS Code is the one Git uses.
    /// </summary>
    public MergeToolPreferencesTests()
    {
        _locator.ScanAsync(Arg.Any<CancellationToken>()).Returns(new MergeToolScan(new[] { _code, _rider, _riderNext }, _code));
        _settings.Get(Arg.Any<string>()).Returns(call => _stored.GetValueOrDefault(call.Arg<string>()));
        _settings.When(store => store.Set(Arg.Any<string>(), Arg.Any<string>())).Do(call => _stored[call.ArgAt<string>(0)] = call.ArgAt<string>(1));
    }

    /// <summary>
    /// Verifies that without a choice the tool the search suggests is used.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Without_A_Choice_The_Suggested_Tool_Is_Used()
    {
        (await Preferences().GetDefaultAsync()).Should().Be(_code);
    }

    /// <summary>
    /// Verifies that the chosen installation is kept, and that another installation of the same tool replaces it when it
    /// was removed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Chosen_Installation_Is_Kept()
    {
        var preferences = Preferences();
        preferences.SetDefault(_riderNext);

        (await Preferences().GetDefaultAsync()).Should().Be(_riderNext);

        _stored[MergeToolPreferences.PATH_SETTING] = @"C:\JetBrains\Rider 2025.3\bin\rider64.exe";
        (await Preferences().GetDefaultAsync())!.ProfileId.Should().Be("rider");
    }

    /// <summary>
    /// Verifies that the tool the user described is kept with its arguments, and is not offered once its executable is gone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Described_Tool_Is_Kept_With_Its_Arguments()
    {
        // In the form of the machine that runs the test: the name of the tool comes from a real path.
        var executable = Path.Combine(Path.GetTempPath(), "My Merge", "merge.exe");
        _machine.FileExists(executable).Returns(true);
        var preferences = Preferences();
        var custom = preferences.CreateCustom(executable, "/out:{result} \"{local}\" {incoming}")!;
        preferences.SetDefault(custom);

        var restored = await Preferences().GetDefaultAsync();

        restored!.IsCustom.Should().BeTrue();
        restored.Name.Should().Be("merge");
        restored.Arguments.Should().Equal("/out:{result}", "{local}", "{incoming}");

        _machine.FileExists(executable).Returns(false);
        (await Preferences().GetDefaultAsync()).Should().BeNull();
    }

    /// <summary>
    /// Verifies that a tool described with an unusable template is not built.
    /// </summary>
    [Fact]
    public void A_Described_Tool_Without_The_Result_Is_Refused()
    {
        _machine.FileExists(@"D:\merge.exe").Returns(true);

        Preferences().CreateCustom(@"D:\merge.exe", "{local} {incoming}").Should().BeNull();
    }

    /// <summary>
    /// Verifies that the machine is searched once, and again only when asked.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Search_Is_Kept_Until_Asked_Again()
    {
        var preferences = Preferences();

        await preferences.GetToolsAsync();
        await preferences.GetDefaultAsync();
        await _locator.Received(1).ScanAsync(Arg.Any<CancellationToken>());

        await preferences.GetToolsAsync(rescan: true);
        await _locator.Received(2).ScanAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creates the preferences.
    /// </summary>
    /// <returns>The preferences.</returns>
    private MergeToolPreferences Preferences() => new(_locator, _settings, _machine);
}
