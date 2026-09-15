using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace RCynQ.Service.Hosting;

/// <summary>
/// Extension points of <see cref="ServiceHostBuilder"/>, used by tests and embedding hosts.
/// </summary>
public sealed class ServiceHostOptions
{
    /// <summary>
    /// Gets or sets a callback that adds configuration sources after the default ones and before options are bound.
    /// </summary>
    public Action<IConfigurationBuilder>? ConfigureConfiguration { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked after all default registrations, right before the application is built.
    /// </summary>
    public Action<WebApplicationBuilder>? ConfigureBuilder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether migrations, configuration seeding and the role pipeline run on start.
    /// </summary>
    public bool RunBackgroundServices { get; set; } = true;
}
