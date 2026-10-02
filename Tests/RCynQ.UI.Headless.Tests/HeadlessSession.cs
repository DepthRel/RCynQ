using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using RCynQ.Localization;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// The headless session every test of the project shares: Avalonia starts once per process.
/// </summary>
internal static class HeadlessSession
{
    /// <summary>
    /// The session, started by the first test that needs it.
    /// </summary>
    private static readonly Lazy<HeadlessUnitTestSession> _session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

    /// <summary>
    /// Gets the session.
    /// </summary>
    public static HeadlessUnitTestSession Value => _session.Value;

    /// <summary>
    /// Gets the localizer of the running application: the one the views are bound to, so the view models of a test have
    /// to be given the same one.
    /// </summary>
    public static ILocalizer AppLocalizer => ((App)Application.Current!).Localizer;

    /// <summary>
    /// Lets the pending bindings and layout passes run.
    /// </summary>
    public static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }
}
