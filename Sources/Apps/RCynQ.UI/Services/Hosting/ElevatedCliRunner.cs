using System.ComponentModel;
using System.Diagnostics;
using RCynQ.Localization;

namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// <see cref="IPeerAccessElevation"/> that starts the RCynQ CLI through the Windows elevation prompt (UAC).
/// </summary>
/// <remarks>
/// The elevated CLI computes the lines itself from the node configuration and database; the UI passes only the
/// configuration path, so no text chosen by the UI process is written with administrator rights.
/// </remarks>
public sealed class ElevatedCliRunner : IPeerAccessElevation
{
    /// <summary>The Windows error code of a declined elevation prompt.</summary>
    private const int CancelledByUser = 1223;

    /// <summary>The configuration file.</summary>
    private readonly string _configPath;

    /// <summary>The CLI launch command, or <see langword="null"/> when the CLI was not found.</summary>
    private readonly ServiceLaunchCommand? _cli;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElevatedCliRunner"/> class.
    /// </summary>
    /// <param name="configPath">The configuration file of the node.</param>
    /// <param name="cli">The CLI launch command, or <see langword="null"/> when the CLI was not found.</param>
    public ElevatedCliRunner(string configPath, ServiceLaunchCommand? cli)
    {
        _configPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        _cli = cli;
    }

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows() && _cli is not null;

    /// <summary>
    /// Builds the argument string for <see cref="ProcessStartInfo.Arguments"/> (shell execution does not support argument lists).
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The quoted argument string.</returns>
    /// <exception cref="ArgumentException">An argument contains a double quote.</exception>
    public static string JoinArguments(IEnumerable<string> arguments)
        => string.Join(' ', arguments.Select(argument =>
        {
            if (argument.Contains('"'))
            {
                throw new ArgumentException("Arguments must not contain double quotes.", nameof(arguments));
            }

            return argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument.TrimEnd('\\')}\"" : argument;
        }));

    /// <inheritdoc />
    public async Task<ElevatedRunResult> AllowPeerAccessAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return new ElevatedRunResult(ElevatedRunOutcome.NotAvailable, Texts.Get("Elevation.Terminal"));
        }

        var info = new ProcessStartInfo(_cli!.FileName, JoinArguments(_cli.PrefixArguments.Concat(new[] { "ssh", "allow", "--config", _configPath })))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException(Texts.Get("Elevation.CliDidNotStart"));
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0
                ? new ElevatedRunResult(ElevatedRunOutcome.Completed, Texts.Get("Elevation.Done"))
                : new ElevatedRunResult(ElevatedRunOutcome.Failed, Texts.Format("Elevation.Failed", process.ExitCode));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == CancelledByUser)
        {
            return new ElevatedRunResult(ElevatedRunOutcome.Cancelled, Texts.Get("Elevation.Cancelled"));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ElevatedRunResult(ElevatedRunOutcome.Failed, Texts.Format("Elevation.StartFailed", ex.Message));
        }
    }
}
