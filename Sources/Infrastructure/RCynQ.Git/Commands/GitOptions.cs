namespace RCynQ.Git.Commands;

/// <summary>
/// Settings of the <c>git</c> process wrapper.
/// </summary>
public sealed class GitOptions
{
    /// <summary>
    /// Gets or sets the <c>git</c> executable name or path.
    /// </summary>
    public string GitExecutable { get; set; } = "git";

    /// <summary>
    /// Gets or sets the timeout for a single git command.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
