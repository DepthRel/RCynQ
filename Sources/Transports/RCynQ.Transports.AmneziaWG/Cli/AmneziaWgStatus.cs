using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Transports.AmneziaWG.Cli;

/// <summary>
/// The state of an AmneziaWG interface.
/// </summary>
/// <param name="Running">Whether the interface is up.</param>
/// <param name="InterfaceName">The interface name derived from the configuration file name.</param>
/// <param name="LocalIp">The local tunnel address from the configuration, or <see langword="null"/>.</param>
/// <param name="Message">An explanation when the interface is down, or <see langword="null"/>.</param>
public sealed record AmneziaWgStatus(bool Running, string InterfaceName, string? LocalIp, string? Message);
