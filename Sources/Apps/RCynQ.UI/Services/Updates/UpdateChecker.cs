using RCynQ.Localization;
using RCynQ.UI.Enums;

namespace RCynQ.UI.Services.Updates;

/// <summary>
/// Default <see cref="IUpdateChecker"/>: reads the list of releases and compares the version of the own platform.
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    /// <summary>The localizer of the reasons of a failure.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The list of releases.</summary>
    private readonly IUpdateFeed _feed;

    /// <summary>The section of the list for this operating system, or <see langword="null"/> where none is published.</summary>
    private readonly string? _platform;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateChecker"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the reasons of a failure.</param>
    /// <param name="feed">The list of releases.</param>
    /// <param name="current">The version that runs.</param>
    /// <param name="platform">The section of the list for this operating system, or <see langword="null"/> where none is published.</param>
    public UpdateChecker(ILocalizer localizer, IUpdateFeed feed, Version current, string? platform)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        Current = current ?? throw new ArgumentNullException(nameof(current));
        _platform = platform;
    }

    /// <inheritdoc />
    public Version Current { get; }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_platform is null)
        {
            return Failed(_localizer.Get("Update.NoPackage"));
        }

        string json;
        try
        {
            json = await _feed.ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidDataException or IOException)
        {
            return Failed(ex is InvalidDataException ? _localizer.Get("Update.BadList") : _localizer.Describe(ex));
        }

        if (UpdateManifest.TryReadVersion(json, _platform) is not { } latest)
        {
            return Failed(_localizer.Get("Update.BadList"));
        }

        return new UpdateCheckResult(latest > Current ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate, latest, null);
    }

    /// <summary>
    /// Makes the result of a failed check.
    /// </summary>
    /// <param name="error">Why it failed.</param>
    /// <returns>The result.</returns>
    private static UpdateCheckResult Failed(string error) => new(UpdateCheckStatus.Failed, null, error);
}
