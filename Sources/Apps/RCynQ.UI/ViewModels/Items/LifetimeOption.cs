namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A lifetime an invitation can be given.
/// </summary>
/// <param name="Hours">The lifetime in hours.</param>
/// <param name="Text">The localized label.</param>
public sealed record LifetimeOption(int Hours, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}
