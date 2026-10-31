using RCynQ.MergeTools;
using RCynQ.MergeTools.Enums;
using RCynQ.UI.Services.Configuration;

namespace RCynQ.UI.Services.Merging;

/// <summary>
/// <see cref="IMergeToolPreferences"/> that keeps the choice in the settings of the interface: the profile of the tool,
/// its executable and, for the tool the user described, the template of its arguments.
/// </summary>
public sealed class MergeToolPreferences : IMergeToolPreferences
{
    /// <summary>The setting with the profile of the chosen tool.</summary>
    public const string TOOL_SETTING = "mergeTool";

    /// <summary>The setting with the executable of the chosen tool.</summary>
    public const string PATH_SETTING = "mergeToolPath";

    /// <summary>The setting with the template of the arguments of the tool the user described.</summary>
    public const string ARGUMENTS_SETTING = "mergeToolArguments";

    /// <summary>Finds the tools of the machine.</summary>
    private readonly IMergeToolLocator _locator;

    /// <summary>The settings of the interface.</summary>
    private readonly IUiSettingsStore _settings;

    /// <summary>The machine, to tell whether the executable of the chosen tool is still there.</summary>
    private readonly IMergeToolEnvironment _environment;

    /// <summary>The kept search, or <see langword="null"/> before the first one.</summary>
    private Task<MergeToolScan>? _scan;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeToolPreferences"/> class.
    /// </summary>
    /// <param name="locator">Finds the tools of the machine.</param>
    /// <param name="settings">The settings of the interface.</param>
    /// <param name="environment">The machine.</param>
    public MergeToolPreferences(IMergeToolLocator locator, IUiSettingsStore settings, IMergeToolEnvironment environment)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MergeTool>> GetToolsAsync(bool rescan = false, CancellationToken cancellationToken = default)
        => (await ScanAsync(rescan, cancellationToken)).Tools;

    /// <inheritdoc />
    public async Task<MergeTool?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var scan = await ScanAsync(rescan: false, cancellationToken);
        var id = _settings.Get(TOOL_SETTING);
        var path = _settings.Get(PATH_SETTING);
        if (id == MergeTool.CUSTOM_ID)
        {
            return CreateCustom(path, _settings.Get(ARGUMENTS_SETTING));
        }

        if (id is not null)
        {
            // The installation the user chose; another installation of the same tool when it was removed.
            var chosen = scan.Tools.FirstOrDefault(tool => tool.ProfileId == id && string.Equals(tool.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)) ??
                scan.Tools.FirstOrDefault(tool => tool.ProfileId == id);
            if (chosen is not null)
            {
                return chosen;
            }
        }

        return scan.Suggested;
    }

    /// <inheritdoc />
    public void SetDefault(MergeTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _settings.Set(TOOL_SETTING, tool.ProfileId);
        _settings.Set(PATH_SETTING, tool.ExecutablePath);
        if (tool.IsCustom)
        {
            _settings.Set(ARGUMENTS_SETTING, MergeToolArguments.Format(tool.Arguments));
        }
    }

    /// <inheritdoc />
    public MergeTool? CreateCustom(string? path, string? arguments)
    {
        var template = MergeToolArguments.Parse(arguments);
        if (string.IsNullOrWhiteSpace(path) ||
            !_environment.FileExists(path) ||
            MergeToolArguments.Validate(template) != MergeToolTemplateProblem.None)
        {
            return null;
        }

        return new MergeTool(MergeTool.CUSTOM_ID, Path.GetFileNameWithoutExtension(path), path, template!, TrustExitCode: false);
    }

    /// <summary>
    /// Searches the machine, or returns the kept search.
    /// </summary>
    /// <param name="rescan">Whether to search again.</param>
    /// <param name="cancellationToken">A token to stop the search.</param>
    /// <returns>The search.</returns>
    private Task<MergeToolScan> ScanAsync(bool rescan, CancellationToken cancellationToken)
    {
        if (rescan || _scan is null || _scan.IsFaulted || _scan.IsCanceled)
        {
            _scan = _locator.ScanAsync(cancellationToken);
        }

        return _scan;
    }
}
