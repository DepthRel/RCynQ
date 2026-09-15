namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// Timing of <see cref="ServiceProcessController"/>.
/// </summary>
public sealed class ServiceControllerOptions
{
    /// <summary>Gets the maximum time the service may take to answer after it was started.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(40);

    /// <summary>Gets the maximum time the service may take to exit after a shutdown request before it is killed.</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets the timeout of the shutdown request itself.</summary>
    public TimeSpan ShutdownRequestTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Gets the interval between health checks while the service starts.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Gets the identifier passed to the service with <c>--parent-pid</c>.</summary>
    public int ParentProcessId { get; init; } = Environment.ProcessId;
}
