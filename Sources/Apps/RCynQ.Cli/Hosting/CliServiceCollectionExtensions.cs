using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Hosting;

/// <summary>
/// Registers the command services of the CLI.
/// </summary>
public static class CliServiceCollectionExtensions
{
    /// <summary>
    /// Registers status, link, transport, patch, Git, diagnostics and export/import command services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddCliCommandServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<StatusCommandService>();
        services.TryAddTransient<LinkCommandService>();
        services.TryAddTransient<RepositoryCommandService>();
        services.TryAddTransient<TransportCommandService>();
        services.TryAddTransient<PatchCommandService>();
        services.TryAddTransient<GitCommandService>();
        services.TryAddTransient<CommitCommandService>();
        services.TryAddTransient<DiagnosticsCommandService>();
        services.TryAddTransient<ExportImportService>();
        return services;
    }
}
