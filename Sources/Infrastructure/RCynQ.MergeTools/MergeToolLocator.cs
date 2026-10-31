using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;
using RCynQ.MergeTools.Enums;

namespace RCynQ.MergeTools;

/// <summary>
/// <see cref="IMergeToolLocator"/> that walks the catalog: the commands in <c>PATH</c>, the usual places of every tool
/// and the installations of Visual Studio <c>vswhere</c> lists; then the tool chosen for Git is put first.
/// </summary>
public sealed class MergeToolLocator : IMergeToolLocator
{
    /// <summary>The place of <c>vswhere</c>, which the Visual Studio installer puts there whatever the version.</summary>
    private const string VSWHERE_PATH = "%ProgramFiles(x86)%/Microsoft Visual Studio/Installer/vswhere.exe";

    /// <summary>The ending of the folder of a macOS application, left out of the name of an installation.</summary>
    private const string APP_BUNDLE_SUFFIX = ".app";

    /// <summary>How long <c>vswhere</c> may take to answer.</summary>
    private static readonly TimeSpan _vswhereTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The machine.</summary>
    private readonly IMergeToolEnvironment _environment;

    /// <summary>Starts <c>vswhere</c>.</summary>
    private readonly IProcessRunner _processes;

    /// <summary>Reads the tool chosen for Git.</summary>
    private readonly GitMergeToolHint _gitHint;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeToolLocator"/> class.
    /// </summary>
    /// <param name="environment">The machine.</param>
    /// <param name="processes">Starts <c>vswhere</c> and Git.</param>
    public MergeToolLocator(IMergeToolEnvironment environment, IProcessRunner processes)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _processes = processes ?? throw new ArgumentNullException(nameof(processes));
        _gitHint = new GitMergeToolHint(processes);
    }

    /// <inheritdoc />
    public async Task<MergeToolScan> ScanAsync(CancellationToken cancellationToken = default)
    {
        var tools = new List<MergeTool>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var profile in MergeToolCatalog.Profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = profile.Discovery == MergeToolDiscovery.VisualStudio
                ? await FindVisualStudioAsync(profile, cancellationToken)
                : FindByPaths(profile);
            tools.AddRange(found.Where(tool => seen.Add(tool.ExecutablePath)));
        }

        var git = await _gitHint.ReadAsync(cancellationToken);
        var preferred = git is null ? null : Prefer(git, tools, seen);
        return new MergeToolScan(tools, preferred ?? tools.FirstOrDefault());
    }

    /// <summary>
    /// Gets the comparer of paths of the machine: Linux tells letter case apart, Windows and macOS do not.
    /// </summary>
    private StringComparer PathComparer => _environment.Platform == MergeToolPlatform.Linux ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Gets the separator of the parts of a path on the machine.
    /// </summary>
    private char Separator => _environment.Platform == MergeToolPlatform.Windows ? '\\' : '/';

    /// <summary>
    /// Finds the tool chosen for Git among the found ones, adding the one at <c>mergetool.&lt;name&gt;.path</c> when it is
    /// elsewhere.
    /// </summary>
    /// <param name="git">The tool chosen for Git.</param>
    /// <param name="tools">The found tools; the one from the Git configuration is added to them.</param>
    /// <param name="seen">The paths of the found tools.</param>
    /// <returns>The tool to offer first, or <see langword="null"/> when RCynQ does not know the tool Git uses.</returns>
    private MergeTool? Prefer(GitMergeToolSetting git, List<MergeTool> tools, HashSet<string> seen)
    {
        var profile = MergeToolCatalog.Profiles.FirstOrDefault(candidate => candidate.GitNames.Contains(git.Name, StringComparer.OrdinalIgnoreCase));
        if (profile is null)
        {
            return null;
        }

        // Git for Windows writes paths with forward slashes; the found tools use the separator of the machine.
        var path = git.Path?.Replace(_environment.Platform == MergeToolPlatform.Windows ? '/' : '\\', Separator);
        if (path is not null && IsAbsolute(path) && _environment.FileExists(path))
        {
            var configured = tools.FirstOrDefault(tool => PathComparer.Equals(tool.ExecutablePath, path));
            if (configured is not null)
            {
                return configured;
            }

            if (profile.Discovery == MergeToolDiscovery.Paths && seen.Add(path))
            {
                configured = new MergeTool(profile.Id, profile.Name, path, profile.Arguments, profile.TrustExitCode);
                tools.Add(configured);
                return configured;
            }
        }

        return tools.FirstOrDefault(tool => tool.ProfileId == profile.Id);
    }

    /// <summary>
    /// Finds the installations of a tool by its command in <c>PATH</c> and by its usual places.
    /// </summary>
    /// <param name="profile">The tool.</param>
    /// <returns>The installations.</returns>
    private IEnumerable<MergeTool> FindByPaths(MergeToolProfile profile)
    {
        foreach (var command in profile.Commands)
        {
            if (FindInPath(command) is { } path)
            {
                yield return new MergeTool(profile.Id, profile.Name, path, profile.Arguments, profile.TrustExitCode);
            }
        }

        foreach (var candidate in profile.Candidates.Where(candidate => candidate.Platform == _environment.Platform))
        {
            foreach (var (path, label) in Resolve(candidate.Path))
            {
                yield return new MergeTool(profile.Id, label ?? profile.Name, path, profile.Arguments, profile.TrustExitCode);
            }
        }
    }

    /// <summary>
    /// Looks a command up in <c>PATH</c>. On Windows only an <c>.exe</c> counts: a <c>.cmd</c> needs a shell to run.
    /// </summary>
    /// <param name="command">The name of the command.</param>
    /// <returns>The full path, or <see langword="null"/>.</returns>
    private string? FindInPath(string command)
    {
        var name = _environment.Platform == MergeToolPlatform.Windows ? command + ".exe" : command;
        var directories = (_environment.GetVariable("PATH") ?? string.Empty)
            .Split(_environment.Platform == MergeToolPlatform.Windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return directories
            .Where(IsAbsolute)
            .Select(directory => directory.TrimEnd('/', '\\') + Separator + name)
            .FirstOrDefault(_environment.FileExists);
    }

    /// <summary>
    /// Finds the installations of Visual Studio through <c>vswhere</c>.
    /// </summary>
    /// <param name="profile">The profile of the merge tool of Visual Studio.</param>
    /// <param name="cancellationToken">A token to stop waiting for <c>vswhere</c>.</param>
    /// <returns>The installations that have the merge tool.</returns>
    private async Task<IReadOnlyList<MergeTool>> FindVisualStudioAsync(MergeToolProfile profile, CancellationToken cancellationToken)
    {
        if (_environment.Platform != MergeToolPlatform.Windows || Resolve(VSWHERE_PATH).FirstOrDefault().Path is not { } vswhere)
        {
            return Array.Empty<MergeTool>();
        }

        string output;
        try
        {
            var result = await _processes.RunAsync(
                new ProcessSpec(vswhere, new[] { "-all", "-prerelease", "-products", "*", "-format", "json", "-utf8" }, null, null, _vswhereTimeout),
                cancellationToken);
            output = result.ExitCode == 0 ? result.StandardOutput : string.Empty;
        }
        catch (SyncBridgeException)
        {
            return Array.Empty<MergeTool>();
        }

        return VisualStudioInstallations.Parse(output)
            .Select(installation => new MergeTool(
                profile.Id,
                installation.Name,
                installation.InstallationPath.TrimEnd('\\', '/') + Separator + VisualStudioInstallations.MERGE_TOOL_PATH,
                profile.Arguments,
                profile.TrustExitCode))
            .Where(tool => _environment.FileExists(tool.ExecutablePath))
            .ToList();
    }

    /// <summary>
    /// Turns a usual place into the files that exist: the variables are filled in and every <c>*</c> is tried with the
    /// folders that match it.
    /// </summary>
    /// <param name="template">The place (see <see cref="MergeToolCandidate"/>).</param>
    /// <returns>The files with the name of their installation when a <c>*</c> picked it, otherwise <see langword="null"/>.</returns>
    private IReadOnlyList<(string Path, string? Label)> Resolve(string template)
    {
        var expanded = ExpandVariables(template);
        if (expanded is null)
        {
            return Array.Empty<(string, string?)>();
        }

        var parts = expanded.Split('/', '\\');
        var roots = new List<(string Path, string? Label)> { (parts[0].Length == 0 ? Separator.ToString() : parts[0] + Separator, null) };
        foreach (var part in parts.Skip(1).Where(part => part.Length > 0))
        {
            var next = new List<(string Path, string? Label)>();
            foreach (var (path, label) in roots)
            {
                if (!part.Contains('*'))
                {
                    next.Add((Join(path, part), label));
                    continue;
                }

                foreach (var directory in _environment.GetDirectories(path, part))
                {
                    var folder = LastPart(directory);
                    var name = folder.EndsWith(APP_BUNDLE_SUFFIX, StringComparison.OrdinalIgnoreCase) ? folder[..^APP_BUNDLE_SUFFIX.Length] : folder;
                    next.Add((Join(path, folder), label is null ? name : $"{label} {name}"));
                }
            }

            roots = next;
        }

        return roots.Where(root => _environment.FileExists(root.Path)).ToList();
    }

    /// <summary>
    /// Fills in <c>~</c> and the <c>%NAME%</c> variables of a place.
    /// </summary>
    /// <param name="template">The place.</param>
    /// <returns>The path, or <see langword="null"/> when a variable is not set.</returns>
    private string? ExpandVariables(string template)
    {
        if (template.StartsWith('~'))
        {
            template = _environment.HomeDirectory.TrimEnd('/', '\\') + template[1..];
        }

        while (template.IndexOf('%') is var start and >= 0)
        {
            var end = template.IndexOf('%', start + 1);
            if (end < 0)
            {
                return null;
            }

            var value = _environment.GetVariable(template[(start + 1)..end]);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            template = template[..start] + value + template[(end + 1)..];
        }

        return template;
    }

    /// <summary>
    /// Tells whether a path is a full one on the machine; a relative entry of <c>PATH</c> or of the Git configuration would
    /// depend on the current folder.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a full path.</returns>
    private bool IsAbsolute(string path) => _environment.Platform == MergeToolPlatform.Windows
        ? path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
        : path.StartsWith('/');

    /// <summary>
    /// Appends a part to a path with the separator of the machine.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="part">The part.</param>
    /// <returns>The longer path.</returns>
    private string Join(string path, string part) => path.EndsWith(Separator) ? path + part : path + Separator + part;

    /// <summary>
    /// Takes the last part of a path, whichever separator it uses.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The last part.</returns>
    private static string LastPart(string path) => path[(path.TrimEnd('/', '\\').LastIndexOfAny(new[] { '/', '\\' }) + 1)..].TrimEnd('/', '\\');
}
