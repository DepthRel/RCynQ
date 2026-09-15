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
/// Settings of the AmneziaWG CLI adapter.
/// </summary>
public sealed class AmneziaWgOptions
{
    /// <summary>Gets or sets the <c>awg-quick</c> executable (Linux, macOS).</summary>
    public string QuickExecutable { get; set; } = "awg-quick";

    /// <summary>Gets or sets the <c>awg</c> executable used for status queries.</summary>
    public string ShowExecutable { get; set; } = "awg";

    /// <summary>Gets or sets the Windows tunnel service executable.</summary>
    public string WindowsExecutable { get; set; } = "amneziawg.exe";

    /// <summary>Gets or sets a value indicating whether the Windows tunnel service commands are used.</summary>
    public bool UseWindowsTunnelService { get; set; } = OperatingSystem.IsWindows();

    /// <summary>Gets or sets the command timeout.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
