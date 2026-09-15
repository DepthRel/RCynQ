using RCynQ.UI.ViewModels.Navigation;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A problem shown on the overview with a quick jump to the responsible tab.
/// </summary>
/// <param name="Text">The problem description.</param>
/// <param name="Target">The tab that helps to solve it.</param>
public sealed record ProblemItemViewModel(string Text, TabKey Target);
