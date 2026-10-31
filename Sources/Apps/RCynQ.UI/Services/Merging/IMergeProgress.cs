using RCynQ.UI.Enums;

namespace RCynQ.UI.Services.Merging;

/// <summary>
/// Where a merge in a merge tool shows how it goes and asks the user when it needs a decision.
/// </summary>
public interface IMergeProgress
{
    /// <summary>
    /// Shows the file that is open in the tool.
    /// </summary>
    /// <param name="number">The number of the file, from 1.</param>
    /// <param name="count">How many files the merge has.</param>
    /// <param name="path">The path of the file in the repository.</param>
    void ShowFile(int number, int count, string path);

    /// <summary>
    /// Shows what the merge does now.
    /// </summary>
    /// <param name="text">The text.</param>
    void ShowStatus(string text);

    /// <summary>
    /// Asks the user what to do.
    /// </summary>
    /// <param name="message">The question.</param>
    /// <param name="choices">The answers offered.</param>
    /// <returns>The answer; <see cref="MergePromptChoice.Cancel"/> when the user gave the merge up.</returns>
    Task<MergePromptChoice> AskAsync(string message, IReadOnlyList<MergePromptChoice> choices);
}
