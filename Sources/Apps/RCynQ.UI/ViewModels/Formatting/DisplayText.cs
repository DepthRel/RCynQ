using System.Globalization;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.ViewModels.Items;

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
    private const string DateTimeFormatKey = "Format.DateTime";

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
    /// <param name="kind">The visual status.</param>
    /// <returns>The label.</returns>
    public static string Link(LinkStatusKind kind) => Texts.Get($"Link.{kind}");

    /// <summary>
    /// Gets the label of a patch status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The label.</returns>
    public static string Patch(PatchStatus status) => Texts.Get($"Patch.{status}");

    /// <summary>
    /// Gets the label of a patch direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The label.</returns>
    public static string Direction(PatchDirection direction) => Texts.Get($"Direction.{direction}");

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
    /// Gets the label of a transport: how the neighbour becomes reachable over the network.
    /// </summary>
    /// <remarks>
    /// The transport is not the same thing as <see cref="EndpointMode"/>, which says how a node accepts
    /// connections. The word "Direct" in <see cref="TransportKind"/> means "no VPN tunnel", not "no SSH".
    /// </remarks>
    /// <param name="kind">The transport.</param>
    /// <returns>The label.</returns>
    public static string Transport(TransportKind kind) => Texts.Get($"Transport.{kind}");

    /// <summary>
    /// Gets the label of a network address kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The label.</returns>
    public static string AddressKind(NetworkAddressKind kind) => Texts.Get($"AddressKind.{kind}");

    /// <summary>
    /// Gets the description of an environment warning.
    /// </summary>
    /// <param name="warning">The warning.</param>
    /// <returns>The description.</returns>
    public static string Warning(EnvironmentWarning warning) => Texts.Get($"Warning.{warning}");

    /// <summary>
    /// Gets the label of a node role in a path field.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The label: the repository of a Dest, the working copy of the others.</returns>
    public static string PathLabel(NodeRole role) => Texts.Get($"Field.Path.{role}");

    /// <summary>
    /// Gets the status bar label of the local service.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The label; empty while the service is not started.</returns>
    public static string Service(ServiceControllerStatus status)
        => status == ServiceControllerStatus.NotStarted ? string.Empty : Texts.Get($"Service.{status}");

    /// <summary>
    /// Formats a timestamp in local time by the format of the current language.
    /// </summary>
    /// <param name="value">The timestamp, or <see langword="null"/>.</param>
    /// <returns>The text, or the word for "never".</returns>
    public static string Time(DateTimeOffset? value)
        => value is null ? Texts.Get("Common.Never") : value.Value.ToLocalTime().ToString(Texts.Get(DateTimeFormatKey), CultureInfo.InvariantCulture);
}
