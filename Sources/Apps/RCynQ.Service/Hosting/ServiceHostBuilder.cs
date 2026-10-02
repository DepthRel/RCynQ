using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Data.DependencyInjection;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Exceptions;
using RCynQ.Git.DependencyInjection;
using RCynQ.Service.Endpoints;
using RCynQ.Service.Events;
using RCynQ.Service.Hubs;
using RCynQ.Service.Localization;
using RCynQ.Service.Logging;
using RCynQ.Service.Maintenance;
using RCynQ.Service.Pipelines;
using RCynQ.Service.Security;
using RCynQ.Service.Startup;
using RCynQ.Ssh.DependencyInjection;
using RCynQ.Core.Enums;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.DependencyInjection;
using RCynQ.Sync.Diagnostics.Endpoint;
using RCynQ.Sync.Endpoint;
using RCynQ.Transports.Abstractions.DependencyInjection;
using RCynQ.Transports.AmneziaWG.DependencyInjection;
using RCynQ.Transports.Direct.DependencyInjection;
using Serilog;
using Serilog.Events;
using RCynQ.Localization;

namespace RCynQ.Service.Hosting;

/// <summary>
/// Composes the RCynQ service: configuration, DI, logging, Kestrel on loopback, API endpoints and the SignalR hub.
/// </summary>
public static class ServiceHostBuilder
{
    /// <summary>
    /// The prefix of environment variables read into configuration (for example <c>RCYNQ_SyncBridge__ListenPort</c>).
    /// </summary>
    public const string EnvironmentPrefix = "RCYNQ_";

    /// <summary>
    /// The maximum accepted request body: the compressed content of one file of a patch, or a compressed manifest.
    /// </summary>
    public const long MaxRequestBodyBytes = 512L * 1024 * 1024;

    /// <summary>
    /// The argument with the identifier of the process that started the service.
    /// </summary>
    public const string ParentProcessArgument = "--parent-pid";

    /// <summary>
    /// The argument with the language of the messages of this process (<c>ru</c>, <c>en</c>); the UI passes the language
    /// the user chose, and requests may ask for another one with <c>Accept-Language</c>.
    /// </summary>
    public const string LanguageArgument = "--language";

    /// <summary>
    /// Reads the parent process identifier from the arguments.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="processId">The parent process identifier.</param>
    /// <returns><see langword="true"/> when a valid identifier was passed.</returns>
    public static bool TryGetParentProcessId(IReadOnlyList<string> args, out int processId)
    {
        ArgumentNullException.ThrowIfNull(args);
        processId = 0;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == ParentProcessArgument)
            {
                return int.TryParse(args[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out processId) && processId > 0;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the service application.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="hostOptions">Optional extension points.</param>
    /// <returns>The built, not started application.</returns>
    /// <exception cref="SyncBridgeException">The configuration is invalid.</exception>
    public static WebApplication Build(string[] args, ServiceHostOptions? hostOptions = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        hostOptions ??= new ServiceHostOptions();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        var localizer = CreateLocalizer(args);
        var configPath = ConfigurationFileEditor.ResolvePath(GetArgument(args, "--config"));
        builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables(EnvironmentPrefix);
        builder.Configuration.AddCommandLine(args);
        hostOptions.ConfigureConfiguration?.Invoke(builder.Configuration);

        var options = BindOptions(builder.Configuration);
        var errors = SyncBridgeOptionsValidator.Validate(options, localizer);
        if (errors.Count > 0)
        {
            throw new SyncBridgeException(localizer.Format("Service.InvalidConfig", configPath, Environment.NewLine, string.Join(Environment.NewLine + " - ", errors)));
        }

        ConfigLocations.ApplyStorageDefaults(options, configPath, ConfigLocations.FindPortableRoot());
        var node = SyncBridgeOptionsValidator.ToNodeConfig(options);
        var paths = new AppDataFactory(options).Create();

        var services = builder.Services;
        services.AddSingleton(options);
        services.AddSingleton(node);
        services.AddSingleton(localizer);
        services.AddSingleton<ILocalizer>(localizer);
        services.AddSingleton(new ServiceRuntimeInfo(configPath));
        services.AddSingleton<InMemoryLogSink>();
        services.AddSingleton<ApiTokenStore>();
        services.AddSingleton<IEventPublisher, SignalREventPublisher>();
        services.AddSingleton<ILocalEndpointController, ServiceLocalEndpointController>();

        services.AddRCynQData()
            .AddRCynQGit()
            .AddRCynQSsh()
            .AddRCynQTransports()
            .AddDirectTransport()
            .AddAmneziaWgTransport()
            .AddRCynQSync()
            .AddRCynQDiagnostics();

        services.AddSingleton<SourcePipeline>();
        services.AddSingleton<DestPipeline>();
        services.AddSingleton<MediatorPipeline>();
        services.AddSingleton<IRolePipelineFactory, RolePipelineFactory>();
        if (hostOptions.RunBackgroundServices)
        {
            services.AddHostedService<DatabaseInitializer>();
            services.AddHostedService<ChainConfigurationSeeder>();
            services.AddHostedService<RolePipelineHostedService>();
            services.AddHostedService<DataRetentionBackgroundService>();
        }

        if (TryGetParentProcessId(args, out var parentProcessId))
        {
            services.AddSingleton(new ParentProcessOptions(parentProcessId));
            services.AddHostedService<ParentProcessWatchdog>();
        }

        services.AddSignalR();

        builder.Host.UseSerilog((_, provider, logger) => logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(paths.LogsDirectory, "rcynq-service-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .WriteTo.Sink(provider.GetRequiredService<InMemoryLogSink>()));

        // In direct mode neighbours have no SSH tunnel to this node, so a second listener is opened on the network
        // with the node's own certificate; the local API stays on loopback and keeps its token.
        var endpointCertificate = options.Endpoint.Mode == EndpointMode.Direct
            ? NodeCertificateStore.GetOrCreate(paths, options.NodeId)
            : null;

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, options.ListenPort);
            if (endpointCertificate is not null)
            {
                kestrel.Listen(
                    IPAddress.Parse(options.Endpoint.BindAddress),
                    options.Endpoint.Port,
                    listen => listen.UseHttps(endpointCertificate));
            }

            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });

        hostOptions.ConfigureBuilder?.Invoke(builder);

        var app = builder.Build();

        // Create the local API token eagerly so the UI and other local clients can read it before the first request.
        app.Services.GetRequiredService<ApiTokenStore>().GetOrCreate();
        app.UseMiddleware<RequestLanguageMiddleware>();
        if (endpointCertificate is not null)
        {
            app.UseMiddleware<PublicEndpointMiddleware>(new PublicEndpointOptions(options.Endpoint.Port));
        }

        app.UseMiddleware<ApiTokenMiddleware>();
        app.MapRCynQApi();
        app.MapHub<SyncBridgeHub>(ApiRoutes.Hub);
        return app;
    }

    /// <summary>
    /// Creates the localizer of the service: its messages come in the language named by <see cref="LanguageArgument"/>,
    /// when the application has texts for it, and in the default language otherwise.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The localizer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    public static RequestLocalizer CreateLocalizer(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return new RequestLocalizer(LanguageCatalog.LoadEmbedded(), GetArgument(args, LanguageArgument));
    }

    /// <summary>
    /// Binds the <c>SyncBridge</c> section.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The options.</returns>
    /// <remarks>
    /// <see cref="SyncBridgeOptions.ExcludedDirectories"/> adds to the built-in names rather than replacing them: a node
    /// that excludes one extra directory must not start scanning build output again. Configuration binding appends to a
    /// list, so the merge only needs the result to be trimmed and deduplicated.
    /// </remarks>
    public static SyncBridgeOptions BindOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SyncBridgeOptions.SectionName);
        var options = new SyncBridgeOptions();
        section.Bind(options);

        var excluded = options.ExcludedDirectories
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        options.ExcludedDirectories.Clear();
        options.ExcludedDirectories.AddRange(excluded);
        return options;
    }

    /// <summary>
    /// Reads the value following a command-line option.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="name">The option name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static string? GetArgument(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == name && i + 1 < args.Count)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
            {
                return args[i][(name.Length + 1)..];
            }
        }

        return null;
    }
}
