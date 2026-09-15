using RCynQ.UI.Services.Hosting;

namespace RCynQ.UI.Tests.Support;

/// <summary>
/// Records started processes and returns <see cref="FakeChildProcess"/> instances.
/// </summary>
public sealed class FakeChildProcessLauncher : IChildProcessLauncher
{
    /// <summary>Gets the recorded starts.</summary>
    public List<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Starts { get; } = new();

    /// <summary>Gets the started processes.</summary>
    public List<FakeChildProcess> Processes { get; } = new();

    /// <summary>Gets or sets the action applied to every new process.</summary>
    public Action<FakeChildProcess>? OnStart { get; set; }

    /// <inheritdoc />
    public IChildProcess Start(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        Starts.Add((fileName, arguments, workingDirectory));
        var process = new FakeChildProcess();
        Processes.Add(process);
        OnStart?.Invoke(process);
        return process;
    }
}
