using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;
using RCynQ.MergeTools;
using RCynQ.MergeTools.Enums;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Merging;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels;

/// <summary>
/// The choice of the merge tool in the settings: the tools found on the machine, and a tool the user describes by its
/// executable and the template of its arguments.
/// </summary>
public sealed partial class MergeToolSettingsViewModel : ObservableObject
{
    /// <summary>The template offered for a tool the user describes.</summary>
    public const string DEFAULT_CUSTOM_ARGUMENTS = "{local} {base} {incoming} {result}";

    /// <summary>The merge tools of the machine and the choice.</summary>
    private readonly IMergeToolPreferences _preferences;

    /// <summary>The dialog service, for the picker of the executable.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Whether the selection is being set by the code, not by the user.</summary>
    private bool _isLoading;

    /// <summary>The selected entry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    [NotifyPropertyChangedFor(nameof(PathText))]
    private MergeToolOptionViewModel? _selected;

    /// <summary>The executable of the tool the user describes.</summary>
    [ObservableProperty]
    private string _customPath = string.Empty;

    /// <summary>The template of the arguments of the tool the user describes.</summary>
    [ObservableProperty]
    private string _customArguments = DEFAULT_CUSTOM_ARGUMENTS;

    /// <summary>What is wrong with the tool the user describes, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    /// <summary>A note about the saved choice, or an empty text.</summary>
    [ObservableProperty]
    private string _savedText = string.Empty;

    /// <summary>Whether the machine is being searched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTools))]
    private bool _isScanning;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeToolSettingsViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="preferences">The merge tools of the machine and the choice.</param>
    /// <param name="dialogs">The dialog service, for the picker of the executable.</param>
    public MergeToolSettingsViewModel(ILocalizer localizer, IMergeToolPreferences preferences, IDialogService dialogs)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    /// <summary>Gets the entries of the list: the found tools and the tool the user describes.</summary>
    public ObservableCollection<MergeToolOptionViewModel> Options { get; } = new();

    /// <summary>Gets a value indicating whether the tool the user describes is selected.</summary>
    public bool IsCustom => Selected?.IsCustom == true;

    /// <summary>Gets the executable of the selected found tool, or an empty text.</summary>
    public string PathText => Selected?.Tool?.ExecutablePath ?? string.Empty;

    /// <summary>Gets a value indicating whether there is an error to show.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>Gets a value indicating whether the search found no tool.</summary>
    public bool HasNoTools => !IsScanning && Options.All(option => option.IsCustom);

    /// <summary>
    /// Searches the machine and shows the tools with the current choice selected.
    /// </summary>
    /// <param name="rescan">Whether to search again instead of using the earlier search.</param>
    /// <returns>A task that completes when the list is shown.</returns>
    public async Task LoadAsync(bool rescan = false)
    {
        IsScanning = true;
        try
        {
            var tools = await _preferences.GetToolsAsync(rescan);
            var current = await _preferences.GetDefaultAsync();

            _isLoading = true;
            Options.Clear();
            foreach (var tool in tools)
            {
                Options.Add(new MergeToolOptionViewModel(tool.Name, tool));
            }

            var custom = new MergeToolOptionViewModel(_localizer.Get("Settings.MergeTool.Custom"), null);
            Options.Add(custom);
            if (current is { IsCustom: true })
            {
                CustomPath = current.ExecutablePath;
                CustomArguments = MergeToolArguments.Format(current.Arguments);
                Selected = custom;
            }
            else
            {
                Selected = Options.FirstOrDefault(option => option.Tool == current) ?? Options[0];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = _localizer.Describe(ex);
        }
        finally
        {
            _isLoading = false;
            IsScanning = false;
        }
    }

    /// <summary>
    /// Saves a found tool as soon as the user picks it; the tool the user describes is saved by <see cref="SaveCustomCommand"/>.
    /// </summary>
    /// <param name="value">The selected entry.</param>
    partial void OnSelectedChanged(MergeToolOptionViewModel? value)
    {
        Error = string.Empty;
        SavedText = string.Empty;
        if (_isLoading || value?.Tool is not { } tool)
        {
            return;
        }

        Save(tool);
    }

    /// <summary>
    /// Searches the machine again, for a tool installed meanwhile.
    /// </summary>
    /// <returns>A task that completes when the list is shown.</returns>
    [RelayCommand]
    private Task RescanAsync() => LoadAsync(rescan: true);

    /// <summary>
    /// Picks the executable of the tool the user describes.
    /// </summary>
    /// <returns>A task that completes when the picker is closed.</returns>
    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _dialogs.PickOpenFileAsync(_localizer.Get("Settings.MergeTool.Browse"));
        if (path is not null)
        {
            CustomPath = path;
        }
    }

    /// <summary>
    /// Checks and saves the tool the user describes.
    /// </summary>
    [RelayCommand]
    private void SaveCustom()
    {
        SavedText = string.Empty;
        var path = CustomPath.Trim();
        if (path.Length == 0 || !Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            Error = _localizer.Get("Settings.MergeTool.NoExecutable");
            return;
        }

        var problem = MergeToolArguments.Validate(MergeToolArguments.Parse(CustomArguments));
        if (problem != MergeToolTemplateProblem.None)
        {
            Error = _localizer.Get(problem switch
            {
                MergeToolTemplateProblem.UnknownPlaceholder => "Settings.MergeTool.UnknownPlaceholder",
                MergeToolTemplateProblem.MissingLocal => "Settings.MergeTool.MissingLocal",
                MergeToolTemplateProblem.MissingIncoming => "Settings.MergeTool.MissingIncoming",
                MergeToolTemplateProblem.MissingResult => "Settings.MergeTool.MissingResult",
                _ => "Settings.MergeTool.Unreadable"
            });
            return;
        }

        if (_preferences.CreateCustom(path, CustomArguments) is { } tool)
        {
            Error = string.Empty;
            Save(tool);
        }
    }

    /// <summary>
    /// Remembers the chosen tool.
    /// </summary>
    /// <param name="tool">The tool.</param>
    private void Save(MergeTool tool)
    {
        try
        {
            _preferences.SetDefault(tool);
            SavedText = _localizer.Format("Settings.MergeTool.Saved", tool.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = _localizer.Describe(ex);
        }
    }
}
