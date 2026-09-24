using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;

namespace RCynQ.Git.Commands;

/// <summary>
/// Runs <c>git</c> commands with byte-exact, non-interactive settings.
/// </summary>
public interface IGitCommandRunner
{
    /// <summary>
    /// Runs a git command.
    /// </summary>
    /// <param name="arguments">The git arguments (without the executable).</param>
    /// <param name="workingDirectory">The working directory, or <see langword="null"/>.</param>
    /// <param name="environment">Additional environment variables, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the command.</param>
    /// <returns>The captured result; non-zero exit codes are returned, not thrown.</returns>
    /// <exception cref="SyncBridgeException">git could not be started or timed out.</exception>
    Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a git command that writes or reads the files the user works with, keeping the line-ending settings of the repository
    /// (<c>core.autocrlf</c>, <c>.gitattributes</c>) and its filters, exactly as the user's own <c>git checkout</c> or
    /// <c>git status</c> would.
    /// </summary>
    /// <param name="arguments">The git arguments (without the executable).</param>
    /// <param name="workingDirectory">The working directory, or <see langword="null"/>.</param>
    /// <param name="environment">Additional environment variables, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the command.</param>
    /// <returns>The captured result; non-zero exit codes are returned, not thrown.</returns>
    /// <exception cref="SyncBridgeException">git could not be started or timed out.</exception>
    Task<ProcessResult> RunInWorkingTreeAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default);
}
