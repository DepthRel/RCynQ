using RCynQ.Ssh.Enums;

namespace RCynQ.Ssh.Server;

/// <summary>
/// A command that runs with administrator rights: the program that asks for them and its arguments.
/// </summary>
/// <param name="Elevation">How the command asks for the rights.</param>
/// <param name="FileName">The program to start: <c>powershell.exe</c>, <c>pkexec</c> or <c>osascript</c>.</param>
/// <param name="Arguments">The arguments, each passed as it is.</param>
public sealed record ElevatedCommand(ElevationKind Elevation, string FileName, IReadOnlyList<string> Arguments);
