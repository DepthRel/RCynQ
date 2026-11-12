using FluentAssertions;
using NSubstitute;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.About;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Updates;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels;

namespace RCynQ.UI.Tests.Updates;

/// <summary>
/// Tests of <see cref="UpdateViewModel"/>: when the application checks, when the banner shows, and that skipping a
/// version does not hide a later one.
/// </summary>
public sealed class UpdateViewModelTests
{
    /// <summary>The checker substitute.</summary>
    private readonly IUpdateChecker _checker = Substitute.For<IUpdateChecker>();

    /// <summary>The preferences substitute.</summary>
    private readonly IUpdatePreferences _preferences = Substitute.For<IUpdatePreferences>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>The clock whose timers the test fires.</summary>
    private readonly ManualTimers _time = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateViewModelTests"/> class with the version 0.2.2 running and the
    /// checks on.
    /// </summary>
    public UpdateViewModelTests()
    {
        _checker.Current.Returns(new Version(0, 2, 2));
        _preferences.CheckEnabled.Returns(true);
    }

    /// <summary>
    /// Verifies that the checks start a while after the start and repeat once a day, and that a newer version shows the
    /// banner, the line of the settings and the hint of the icon.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Newer_Version_Is_Found_By_The_Timer_And_Shown()
    {
        Found("0.3.0");
        using var updates = Create();

        updates.Start();
        updates.StatusText.Should().Be(TestTexts.Russian.Get("Update.NotChecked"));
        _time.Timer.Should().NotBeNull();
        _time.DueTime.Should().Be(UpdateViewModel.FirstCheckDelay);
        _time.Period.Should().Be(TimeSpan.FromDays(1));

        await _time.FireAsync();

        updates.IsAvailable.Should().BeTrue();
        updates.IsBannerVisible.Should().BeTrue();
        updates.BannerText.Should().Be(TestTexts.Russian.Format("Update.Available", "0.3.0", "0.2.2"));
        updates.StatusText.Should().Be(TestTexts.Russian.Format("Update.Outdated", "0.3.0", "0.2.2"));
        updates.IsOutdated.Should().BeTrue("the line of the settings is red");
        updates.IsUpToDate.Should().BeFalse();
        updates.TrayToolTip.Should().Be(TestTexts.Russian.Format("Update.Tray", "0.3.0"));
    }

    /// <summary>
    /// Verifies that a skipped version hides the banner but stays in the settings, and that a later version is offered
    /// again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Skipped_Version_Is_Hidden_But_A_Later_One_Is_Offered()
    {
        Found("0.3.0");
        using var updates = Create();
        await updates.CheckNowCommand.ExecuteAsync(null);

        updates.SkipCommand.Execute(null);
        _preferences.Received(1).Skip(new Version(0, 3, 0));
        _preferences.SkippedVersion.Returns(new Version(0, 3, 0));
        updates.IsBannerVisible.Should().BeFalse();
        updates.IsAvailable.Should().BeTrue("the settings still tell about it");

        Found("0.3.1");
        await updates.CheckNowCommand.ExecuteAsync(null);

        updates.IsBannerVisible.Should().BeTrue("a version later than the skipped one is offered again");
    }

    /// <summary>
    /// Verifies that the latest version and a failed check show no banner, and that only the settings tell why it failed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Latest_Version_And_Failures_Show_No_Banner()
    {
        _checker.CheckAsync(Arg.Any<CancellationToken>()).Returns(new UpdateCheckResult(UpdateCheckStatus.UpToDate, new Version(0, 2, 2), null));
        using var updates = Create();
        await updates.CheckNowCommand.ExecuteAsync(null);
        updates.IsBannerVisible.Should().BeFalse();
        updates.StatusText.Should().Be(TestTexts.Russian.Get("Update.UpToDate"));
        updates.IsUpToDate.Should().BeTrue("the line of the settings is green");
        updates.IsOutdated.Should().BeFalse();
        updates.TrayToolTip.Should().Be("RCynQ");

        _checker.CheckAsync(Arg.Any<CancellationToken>()).Returns(new UpdateCheckResult(UpdateCheckStatus.Failed, null, "offline"));
        await updates.CheckNowCommand.ExecuteAsync(null);
        updates.IsBannerVisible.Should().BeFalse();
        updates.StatusText.Should().Be(TestTexts.Russian.Format("Update.Failed", "offline"));
        updates.IsUpToDate.Should().BeFalse();
        updates.IsOutdated.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the checks do not run by themselves while they are off, that a check on demand still works, and that
    /// turning them on stores the choice and starts the timer.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Turned_Off_Checks_Run_Only_On_Demand()
    {
        _preferences.CheckEnabled.Returns(false);
        Found("0.3.0");
        using var updates = Create();

        updates.Start();
        _time.Timer.Should().BeNull();
        await updates.CheckNowCommand.ExecuteAsync(null);
        updates.IsAvailable.Should().BeTrue();

        updates.CheckEnabled = true;
        _preferences.Received(1).SetCheckEnabled(true);
        _preferences.CheckEnabled.Returns(true);
        updates.CheckEnabled = false;
        _preferences.Received(1).SetCheckEnabled(false);
    }

    /// <summary>
    /// Verifies that the website opens at the downloads of the project, whatever the list of releases named.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Website_Opens_At_The_Downloads_Of_The_Project()
    {
        using var updates = Create();

        await updates.OpenSiteCommand.ExecuteAsync(null);

        await _dialogs.Received(1).OpenUriAsync(new Uri(ProjectSite.DOWNLOAD_URL));
    }

    /// <summary>
    /// Makes the checker find a published version.
    /// </summary>
    /// <param name="version">The version.</param>
    private void Found(string version)
        => _checker.CheckAsync(Arg.Any<CancellationToken>()).Returns(new UpdateCheckResult(UpdateCheckStatus.Available, Version.Parse(version), null));

    /// <summary>
    /// Creates the view model under test.
    /// </summary>
    /// <returns>The view model.</returns>
    private UpdateViewModel Create() => new(TestTexts.Russian, _checker, _preferences, _dialogs, new ImmediateUiDispatcher(), _time);

    /// <summary>
    /// A clock whose single timer the test fires by hand.
    /// </summary>
    private sealed class ManualTimers : TimeProvider
    {
        /// <summary>Gets the callback of the last timer, or <see langword="null"/> when none was made.</summary>
        public TimerCallback? Timer { get; private set; }

        /// <summary>Gets the first delay of the last timer.</summary>
        public TimeSpan DueTime { get; private set; }

        /// <summary>Gets the period of the last timer.</summary>
        public TimeSpan Period { get; private set; }

        /// <summary>State passed to the callback of the last timer.</summary>
        private object? _state;

        /// <inheritdoc />
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timer = callback;
            _state = state;
            DueTime = dueTime;
            Period = period;
            return new NoTimer(this);
        }

        /// <summary>
        /// Fires the last timer and lets the check it started finish.
        /// </summary>
        /// <returns>A task that completes after the check.</returns>
        public async Task FireAsync()
        {
            Timer!(_state);
            await Task.Yield();
        }

        /// <summary>
        /// A timer that never fires by itself and forgets its callback when disposed.
        /// </summary>
        private sealed class NoTimer : ITimer
        {
            /// <summary>The clock that made it.</summary>
            private readonly ManualTimers _owner;

            /// <summary>
            /// Initializes a new instance of the <see cref="NoTimer"/> class.
            /// </summary>
            /// <param name="owner">The clock that made it.</param>
            public NoTimer(ManualTimers owner)
            {
                _owner = owner;
            }

            /// <inheritdoc />
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            /// <inheritdoc />
            public void Dispose() => _owner.Timer = null;

            /// <inheritdoc />
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
