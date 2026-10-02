using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Transports.AmneziaWG.Cli;

/// <summary>
/// <see cref="IAmneziaWgCli"/> that invokes <c>awg-quick</c>/<c>awg</c> or the Windows tunnel service.
/// </summary>
/// <remarks>
/// The interface name is the configuration file name without extension, restricted to 15 safe characters (the Linux
/// interface name limit). Bringing interfaces up and down needs administrative privileges; RCynQ never elevates itself.
/// </remarks>
public sealed partial class ProcessAmneziaWgCli : IAmneziaWgCli
{
    /// <summary>The process runner.</summary>
    private readonly IProcessRunner _processRunner;

    /// <summary>The options.</summary>
    private readonly AmneziaWgOptions _options;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessAmneziaWgCli"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="processRunner">The process runner.</param>
    /// <param name="options">The options.</param>
    public ProcessAmneziaWgCli(ILocalizer localizer, IProcessRunner processRunner, AmneziaWgOptions options)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _processRunner = processRunner;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<AmneziaWgStatus> StartAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var (path, name) = Validate(configPath);
        var result = _options.UseWindowsTunnelService
            ? await RunAsync(_options.WindowsExecutable, cancellationToken, "/installtunnelservice", path)
            : await RunAsync(_options.QuickExecutable, cancellationToken, "up", path);
        if (result.ExitCode != 0 && !result.StandardError.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            throw new SyncBridgeException(_localizer.Format("Awg.StartFailed", name, result.StandardError.Trim()));
        }

        return await GetStatusAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StopAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var (path, name) = Validate(configPath);
        var result = _options.UseWindowsTunnelService
            ? await RunAsync(_options.WindowsExecutable, cancellationToken, "/uninstalltunnelservice", name)
            : await RunAsync(_options.QuickExecutable, cancellationToken, "down", path);
        var notRunning = result.StandardError.Contains("is not a", StringComparison.OrdinalIgnoreCase) ||
            result.StandardError.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        if (result.ExitCode != 0 && !notRunning)
        {
            throw new SyncBridgeException(_localizer.Format("Awg.StopFailed", name, result.StandardError.Trim()));
        }
    }

    /// <inheritdoc />
    public async Task<AmneziaWgStatus> GetStatusAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var (path, name) = Validate(configPath);
        var localIp = await ReadAddressAsync(path, cancellationToken);
        try
        {
            var result = await RunAsync(_options.ShowExecutable, cancellationToken, "show", name);
            var running = result.ExitCode == 0 && result.StandardOutput.Contains("interface:", StringComparison.OrdinalIgnoreCase);
            return new AmneziaWgStatus(running, name, localIp, running ? null : NonEmpty(result.StandardError, _localizer.Format("Awg.NotUp", name)));
        }
        catch (SyncBridgeException ex)
        {
            return new AmneziaWgStatus(false, name, localIp, _localizer.Format("Awg.ToolsMissing", _localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Derives and validates the interface name from a configuration path.
    /// </summary>
    /// <param name="configPath">The configuration path.</param>
    /// <returns>The interface name.</returns>
    /// <exception cref="ArgumentException">The name is not a valid interface name.</exception>
    public static string GetInterfaceName(string configPath)
    {
        var name = Path.GetFileNameWithoutExtension(configPath ?? string.Empty);
        if (!InterfaceName().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not a valid interface name (1-15 of letters, digits, '_', '=', '+', '.', '-').", nameof(configPath));
        }

        return name;
    }

    /// <summary>
    /// Validates the configuration path.
    /// </summary>
    /// <param name="configPath">The configuration path.</param>
    /// <returns>The full path and interface name.</returns>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    private static (string Path, string Name) Validate(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !Path.IsPathFullyQualified(configPath))
        {
            throw new ArgumentException($"AmneziaWG configuration path '{configPath}' must be absolute.", nameof(configPath));
        }

        if (!configPath.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"AmneziaWG configuration '{configPath}' must be a .conf file.", nameof(configPath));
        }

        if (!File.Exists(configPath))
        {
            throw new ArgumentException($"AmneziaWG configuration '{configPath}' does not exist.", nameof(configPath));
        }

        return (Path.GetFullPath(configPath), GetInterfaceName(configPath));
    }

    /// <summary>
    /// Reads the first <c>Address</c> entry of a configuration file.
    /// </summary>
    /// <param name="path">The configuration path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The address without prefix length, or <see langword="null"/>.</returns>
    private static async Task<string?> ReadAddressAsync(string path, CancellationToken cancellationToken)
    {
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            var match = AddressLine().Match(line);
            if (match.Success)
            {
                return match.Groups["address"].Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs an AmneziaWG command.
    /// </summary>
    /// <param name="executable">The executable.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The result.</returns>
    private Task<ProcessResult> RunAsync(string executable, CancellationToken cancellationToken, params string[] arguments)
        => _processRunner.RunAsync(new ProcessSpec(executable, arguments, null, null, _options.CommandTimeout), cancellationToken);

    /// <summary>
    /// Returns the trimmed text or a fallback when it is empty.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="fallback">The fallback.</param>
    /// <returns>The result.</returns>
    private static string NonEmpty(string text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    /// <summary>
    /// Pattern for interface names.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9_=+.-]{1,15}$")]
    private static partial Regex InterfaceName();

    /// <summary>
    /// Pattern for <c>Address = a.b.c.d/nn</c> lines.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex(@"^\s*Address\s*=\s*(?<address>[0-9A-Fa-f:.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddressLine();
}
