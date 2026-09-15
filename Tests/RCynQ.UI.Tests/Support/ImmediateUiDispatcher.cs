using RCynQ.UI.Services.Dispatching;

namespace RCynQ.UI.Tests.Support;

/// <summary>
/// Runs posted actions synchronously on the calling thread.
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => action();
}
