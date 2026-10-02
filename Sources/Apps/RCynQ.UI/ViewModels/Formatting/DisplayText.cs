using System.Globalization;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.UI.Enums;

namespace RCynQ.UI.ViewModels.Formatting;

/// <summary>
/// Display texts of domain values in the language the user chose. A text is found by a key built from the name of the
/// value (<c>Patch.Applied</c>), so a new value or a new language is only a new entry in a language file.
/// </summary>
public static class DisplayText
{
    /// <summary>
    /// The key of the format of a timestamp; it differs by language.
    /// </summary>
    private const string DATE_TIME_FORMAT_KEY = "Format.DateTime";

    /// <summary>
    /// Maps a link status to a visual status.
    /// </summary>
    /// <param name="status">The link status.</param>
    /// <returns>The visual status.</returns>
    public static LinkStatusKind LinkKind(LinkStatus status) => status switch
    {
        LinkStatus.Ready => LinkStatusKind.Online,
        LinkStatus.Degraded => LinkStatusKind.Degraded,
        LinkStatus.Failed => LinkStatusKind.Offline,
        _ => LinkStatusKind.Waiting
    };

    /// <summary>
    /// Gets the label of a visual link status.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="kind">The visual status.</param>
    /// <returns>The label.</returns>
    public static string Link(ILocalizer localizer, LinkStatusKind kind) => localizer.Get($"Link.{kind}");

    /// <summary>
    /// Gets the label of a patch status.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="status">The status.</param>
    /// <returns>The label.</returns>
    public static string Patch(ILocalizer localizer, PatchStatus status) => localizer.Get($"Patch.{status}");

    /// <summary>
    /// Gets the label of a patch direction.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The label.</returns>
    public static string Direction(ILocalizer localizer, PatchDirection direction) => localizer.Get($"Direction.{direction}");

    /// <summary>
    /// Gets the one-letter marker of a change kind: the letters of <c>git status</c>, the same in every language.
    /// </summary>
    /// <param name="kind">The change kind.</param>
    /// <returns>The marker.</returns>
    public static string Change(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Modified => "M",
        ChangeKind.Deleted => "D",
        _ => "R"
    };

    /// <summary>
    /// Words a count with the plural forms of the text of a key.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="key">The key of a text with the forms separated by <c>|</c>, each using <c>{0}</c> for the count: two forms
    /// (one, other) or three (one, few, many, as in Slavic languages).</param>
    /// <param name="count">The count.</param>
    /// <returns>The text with the form that fits the count.</returns>
    public static string Count(ILocalizer localizer, string key, int count)
    {
        var forms = localizer.Get(key).Split('|');
        var last = Math.Abs(count) % 10;
        var lastTwo = Math.Abs(count) % 100;
        var index = forms.Length switch
        {
            >= 3 when last == 1 && lastTwo != 11 => 0,
            >= 3 when last is >= 2 and <= 4 && lastTwo is < 12 or > 14 => 1,
            >= 3 => 2,
            _ => count == 1 ? 0 : forms.Length - 1
        };
        return string.Format(CultureInfo.InvariantCulture, forms[index], count);
    }

    /// <summary>
    /// Gets the label of a transport: how the neighbour becomes reachable over the network.
    /// </summary>
    /// <remarks>
    /// The transport is not the same thing as <see cref="EndpointMode"/>, which says how a node accepts
    /// connections. The word "Direct" in <see cref="TransportKind"/> means "no VPN tunnel", not "no SSH".
    /// </remarks>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="kind">The transport.</param>
    /// <returns>The label.</returns>
    public static string Transport(ILocalizer localizer, TransportKind kind) => localizer.Get($"Transport.{kind}");

    /// <summary>
    /// Gets the label of a network address kind.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The label.</returns>
    public static string AddressKind(ILocalizer localizer, NetworkAddressKind kind) => localizer.Get($"AddressKind.{kind}");

    /// <summary>
    /// Gets the description of an environment warning.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="warning">The warning.</param>
    /// <returns>The description.</returns>
    public static string Warning(ILocalizer localizer, EnvironmentWarning warning) => localizer.Get($"Warning.{warning}");

    /// <summary>
    /// Gets the label of a node role in a path field.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="role">The role.</param>
    /// <returns>The label: the repository of an Origin, the working copy of the others.</returns>
    public static string PathLabel(ILocalizer localizer, NodeRole role) => localizer.Get($"Field.Path.{role}");

    /// <summary>
    /// Gets the status bar label of the local service.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="status">The status.</param>
    /// <returns>The label; empty while the service is not started.</returns>
    public static string Service(ILocalizer localizer, ServiceControllerStatus status)
        => status == ServiceControllerStatus.NotStarted ? string.Empty : localizer.Get($"Service.{status}");

    /// <summary>
    /// Formats a timestamp in local time by the format of the current language.
    /// </summary>
    /// <param name="localizer">The localizer of the reader.</param>
    /// <param name="value">The timestamp, or <see langword="null"/>.</param>
    /// <returns>The text, or the word for "never".</returns>
    public static string Time(ILocalizer localizer, DateTimeOffset? value)
        => value is null ? localizer.Get("Common.Never") : value.Value.ToLocalTime().ToString(localizer.Get(DATE_TIME_FORMAT_KEY), CultureInfo.InvariantCulture);
}
