namespace RCynQ.Service.Hosting;

/// <summary>
/// The process that started the service (normally the UI); the service stops when it exits.
/// </summary>
/// <param name="ProcessId">The parent process identifier passed with <c>--parent-pid</c>.</param>
public sealed record ParentProcessOptions(int ProcessId);
