namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Names the command the SSH server runs for a one-time enrollment key: the command line tool of this installation with
/// <c>enroll</c> and the configuration of this node.
/// </summary>
public interface IEnrollmentCommandSource
{
    /// <summary>
    /// Gets the words of the command.
    /// </summary>
    /// <returns>The words, or <see langword="null"/> when the command line tool was not found.</returns>
    IReadOnlyList<string>? GetCommand();
}
