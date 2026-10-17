namespace RCynQ.Ssh.Server;

/// <summary>
/// How a command with administrator rights ended.
/// </summary>
/// <param name="ExitCode">The exit code of the command.</param>
/// <param name="StandardError">What the command wrote to its error output; empty when it could not be read (UAC).</param>
/// <param name="Declined">Whether the user dismissed the Windows UAC prompt, so the command never ran.</param>
public sealed record ElevatedProcessExit(int ExitCode, string StandardError, bool Declined = false);
