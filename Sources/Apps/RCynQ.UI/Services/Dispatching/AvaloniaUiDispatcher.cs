using Avalonia.Threading;

namespace RCynQ.UI.Services.Dispatching;

/// <summary>
/// <see cref="IUiDispatcher"/> using the Avalonia UI thread dispatcher.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
