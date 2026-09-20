using RCynQ.Core.Enums;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One choice of <see cref="EndpointMode"/> shown in the settings.
/// </summary>
/// <param name="Value">The mode.</param>
/// <param name="Title">The title shown in the list.</param>
/// <param name="Hint">The explanation shown under the list.</param>
public sealed record EndpointModeOption(EndpointMode Value, string Title, string Hint)
{
    /// <summary>
    /// Gets the choices in the order they are offered.
    /// </summary>
    public static IReadOnlyList<EndpointModeOption> All { get; } = new[]
    {
        new EndpointModeOption(
            EndpointMode.SshTunnel,
            "Через SSH-сервер этого компьютера",
            "На компьютере должен быть установлен и запущен SSH-сервер (sshd). Соседи подключаются к нему, свой порт RCynQ не открывает."),
        new EndpointModeOption(
            EndpointMode.Direct,
            "Без SSH-сервера: собственный порт с TLS",
            "Для компьютеров, где SSH-сервер установить нельзя. RCynQ откроет свой порт с сертификатом узла; соседи проверяют отпечаток сертификата из файла экспорта. Порт нужно разрешить в брандмауэре.")
    };

    /// <summary>
    /// Gets the choice of a mode.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The choice.</returns>
    public static EndpointModeOption For(EndpointMode mode) => All.First(option => option.Value == mode);

    /// <inheritdoc />
    public override string ToString() => Title;
}
