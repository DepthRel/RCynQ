using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Migrations.SQLite.Runner;
using RCynQ.Core.Enums;
using RCynQ.Service.Hosting;
using RCynQ.Service.Security;

namespace RCynQ.Service.Tests.Support;

/// <summary>
/// Runs the real service composition on Kestrel bound to a free loopback port with a migrated temporary database.
/// </summary>
/// <remarks>
/// A real server is used instead of the in-memory test server so the tests do not depend on the installed ASP.NET Core
/// runtime version matching the test host package.
/// </remarks>
public sealed class ServiceTestHost : IAsyncDisposable
{
    /// <summary>The link token configured for the node.</summary>
    public const string LINK_TOKEN = "service-tests-link-token-0123456789";

    /// <summary>
    /// The name of the repository of the hosted node.
    /// </summary>
    public const string REPOSITORY_NAME = "app";

    /// <summary>The application.</summary>
    private readonly WebApplication _app;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceTestHost"/> class.
    /// </summary>
    /// <param name="app">The started application.</param>
    /// <param name="root">The temporary root directory.</param>
    /// <param name="port">The loopback port.</param>
    private ServiceTestHost(WebApplication app, string root, int port)
    {
        _app = app;
        Root = root;
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
    }

    /// <summary>Gets the temporary root directory.</summary>
    public string Root { get; }

    /// <summary>Gets the base address of the service.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets the configuration file path the service uses.</summary>
    public string ConfigPath => Path.Combine(Root, SyncBridgeOptions.DEFAULT_FILE_NAME);

    /// <summary>Gets the application services.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Gets the local API token.</summary>
    public string ApiToken => Services.GetRequiredService<ApiTokenStore>().GetOrCreate();

    /// <summary>
    /// Starts a service for a role.
    /// </summary>
    /// <param name="role">The node role.</param>
    /// <param name="configureServices">Service replacements applied after the default registrations.</param>
    /// <param name="extraSettings">Additional configuration values, for example a direct TLS endpoint.</param>
    /// <returns>The started host.</returns>
    public static async Task<ServiceTestHost> StartAsync(NodeRole role = NodeRole.Origin, Action<IServiceCollection>? configureServices = null, IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "rcynq-service-tests", Guid.NewGuid().ToString("N"));
        var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        var dataRoot = Path.Combine(root, "data");
        var port = FreePort();
        var settings = new Dictionary<string, string?>();
        if (role != NodeRole.Relay)
        {
            settings["SyncBridge:Repositories:0:Name"] = REPOSITORY_NAME;
            settings["SyncBridge:Repositories:0:Path"] = work;
        }

        settings = new Dictionary<string, string?>(settings)
        {
            ["SyncBridge:NodeId"] = role == NodeRole.Workstation ? "workstation-1" : role == NodeRole.Origin ? "origin-1" : "relay-1",
            ["SyncBridge:Role"] = role.ToString(),

            ["SyncBridge:DataRoot"] = dataRoot,
            ["SyncBridge:LinkToken"] = LINK_TOKEN,
            ["SyncBridge:ListenPort"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        foreach (var pair in extraSettings ?? new Dictionary<string, string?>())
        {
            settings[pair.Key] = pair.Value;
        }

        SqliteDatabaseMigrator.MigrateUp(AppDataPaths.FromRoot(dataRoot).ConnectionString);
        var app = ServiceHostBuilder.Build(
            new[] { "--config", Path.Combine(root, SyncBridgeOptions.DEFAULT_FILE_NAME) },
            new ServiceHostOptions
            {
                RunBackgroundServices = false,
                ConfigureConfiguration = configuration => configuration.AddInMemoryCollection(settings),
                ConfigureBuilder = builder => configureServices?.Invoke(builder.Services)
            });

        await app.StartAsync();
        return new ServiceTestHost(app, root, port);
    }

    /// <summary>
    /// Creates an HTTP client for the service.
    /// </summary>
    /// <param name="withApiToken">Whether the local API token header is added.</param>
    /// <returns>The client.</returns>
    public HttpClient CreateClient(bool withApiToken = true)
    {
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = BaseAddress };
        if (withApiToken)
        {
            client.DefaultRequestHeaders.Add(ApiHeaders.API_TOKEN, ApiToken);
        }

        return client;
    }

    /// <summary>
    /// Stops the service and deletes the temporary directory.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Log files may still be flushed; best effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Finds a free loopback port.
    /// </summary>
    /// <returns>The port.</returns>
    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
