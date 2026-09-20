using System.Globalization;
using RCynQ.Core.Enums;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Formatting;

/// <summary>
/// Russian display texts of domain values.
/// </summary>
public static class DisplayText
{
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
    public static string Link(LinkStatusKind kind) => kind switch
    {
        LinkStatusKind.Online => "онлайн",
        LinkStatusKind.Degraded => "деградация",
        LinkStatusKind.Offline => "нет связи",
        _ => "ожидание"
    };

    /// <summary>
    /// Gets the label of a patch status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The label.</returns>
    public static string Patch(PatchStatus status) => status switch
    {
        PatchStatus.Pending => "в ожидании",
        PatchStatus.Applied => "применён",
        PatchStatus.Rejected => "отклонён",
        PatchStatus.ConflictPending => "конфликт",
        PatchStatus.ConflictResolved => "конфликт разрешён",
        _ => "ошибка"
    };

    /// <summary>
    /// Gets the label of a patch direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The label.</returns>
    public static string Direction(PatchDirection direction) => direction == PatchDirection.Incoming ? "входящий" : "исходящий";

    /// <summary>
    /// Gets the one-letter marker of a change kind.
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
    public static string Transport(TransportKind kind) => kind switch
    {
        TransportKind.Direct => "Обычная сеть (без VPN)",
        TransportKind.AmneziaWG => "AmneziaWG (VPN)",
        TransportKind.WireGuard => "WireGuard (VPN)",
        TransportKind.OpenConnect => "OpenConnect (VPN)",
        TransportKind.Tailscale => "Tailscale (VPN)",
        _ => kind.ToString()
    };

    /// <summary>
    /// Gets the label of a network address kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The label.</returns>
    public static string AddressKind(NetworkAddressKind kind) => kind switch
    {
        NetworkAddressKind.Tunnel => "VPN / туннель",
        NetworkAddressKind.Private => "локальная сеть",
        NetworkAddressKind.Public => "публичный",
        NetworkAddressKind.LinkLocal => "link-local",
        _ => "loopback"
    };

    /// <summary>
    /// Gets the description of an environment warning.
    /// </summary>
    /// <param name="warning">The warning.</param>
    /// <returns>The description.</returns>
    public static string Warning(EnvironmentWarning warning) => warning switch
    {
        EnvironmentWarning.SshdNotListening => "SSH-сервер (sshd) не принимает подключения — соседи не смогут подключиться к этому узлу.",
        EnvironmentWarning.NoReachableAddress => "Ни на одном сетевом адресе SSH не отвечает. Проверьте, что sshd запущен и слушает внешние интерфейсы; файрвол и NAT между узлами отсюда не проверить.",
        EnvironmentWarning.NoSshPublicKey => "SSH-ключ узла не найден — создайте его кнопкой «Создать ключ».",
        EnvironmentWarning.ServicePortNotListening => "Порт сервиса RCynQ не отвечает.",
        EnvironmentWarning.HostKeysUnavailable => "Не удалось прочитать ключи хоста sshd (ssh-keyscan).",
        EnvironmentWarning.EndpointNotListening => "Порт RCynQ для соседей не отвечает ни на одном адресе. Проверьте, что сервис запущен в режиме «Без SSH-сервера», и разрешите порт во входящих правилах брандмауэра.",
        _ => warning.ToString()
    };

    /// <summary>
    /// Gets the status bar label of the local service.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The label.</returns>
    public static string Service(Services.Hosting.ServiceControllerStatus status) => status switch
    {
        Services.Hosting.ServiceControllerStatus.Starting => "сервис запускается",
        Services.Hosting.ServiceControllerStatus.Running => "сервис запущен приложением",
        Services.Hosting.ServiceControllerStatus.Attached => "используется внешний сервис",
        Services.Hosting.ServiceControllerStatus.Stopped => "сервис остановлен",
        Services.Hosting.ServiceControllerStatus.Failed => "сервис не запущен",
        _ => string.Empty
    };

    /// <summary>
    /// Formats a timestamp in local time.
    /// </summary>
    /// <param name="value">The timestamp, or <see langword="null"/>.</param>
    /// <returns>The text, or <c>никогда</c>.</returns>
    public static string Time(DateTimeOffset? value)
        => value is null ? "никогда" : value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
}
