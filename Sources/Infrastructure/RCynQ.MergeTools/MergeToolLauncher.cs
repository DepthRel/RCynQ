using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.MergeTools;

/// <summary>
/// <see cref="IMergeToolLauncher"/> that starts the executable of the tool directly, never through a shell, with every
/// argument passed as it is.
/// </summary>
public sealed class MergeToolLauncher : IMergeToolLauncher
{
    /// <summary>Starts the process of the tool.</summary>
    private readonly IMergeToolProcessStarter _processes;

    /// <summary>The machine.</summary>
    private readonly IMergeToolEnvironment _environment;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeToolLauncher"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="processes">Starts the process of the tool.</param>
    /// <param name="environment">The machine.</param>
    public MergeToolLauncher(ILocalizer localizer, IMergeToolProcessStarter processes, IMergeToolEnvironment environment)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <inheritdoc />
    public IRunningProcess Start(MergeTool tool, MergeToolFiles files, MergeToolLabels labels)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(labels);
        if (!_environment.FileExists(tool.ExecutablePath))
        {
            throw new SyncBridgeException(_localizer.Format("MergeTool.Missing", tool.Name, tool.ExecutablePath));
        }

        var arguments = MergeToolArguments.Expand(tool.Arguments, files, labels);

        // The folder of the result is the working folder: a tool that writes anything next to its files writes it there.
        return _processes.Start(new ProcessSpec(tool.ExecutablePath, arguments, Path.GetDirectoryName(files.Result), null, null));
    }
}
