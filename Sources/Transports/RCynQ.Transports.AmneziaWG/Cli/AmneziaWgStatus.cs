namespace RCynQ.Transports.AmneziaWG.Cli;

/// <summary>
/// The state of an AmneziaWG interface.
/// </summary>
/// <param name="Running">Whether the interface is up.</param>
/// <param name="InterfaceName">The interface name derived from the configuration file name.</param>
/// <param name="LocalIp">The local tunnel address from the configuration, or <see langword="null"/>.</param>
/// <param name="Message">An explanation when the interface is down, or <see langword="null"/>.</param>
public sealed record AmneziaWgStatus(bool Running, string InterfaceName, string? LocalIp, string? Message);
