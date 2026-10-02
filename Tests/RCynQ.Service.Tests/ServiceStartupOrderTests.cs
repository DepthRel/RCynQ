using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Service.Hosting;
using RCynQ.Service.Pipelines;
using RCynQ.Service.Tests.Support;

namespace RCynQ.Service.Tests;

/// <summary>
/// Tests that the local API answers while the role pipeline still warms up.
/// </summary>
/// <remarks>
/// The UI starts the service as a child process and polls <c>GET /health</c> for a limited time before it gives up and
/// kills the process. ASP.NET Core registers its own <c>GenericWebHostService</c>, the one that binds Kestrel, while the
/// application is built, that is after every <c>AddHostedService</c> call of the composition; work done in
/// <c>IHostedService.StartAsync</c> therefore delays the listener. The role pipeline scans the whole working tree and
/// exports the sync base, which takes far longer than the timeout of the UI, so it must not hold the listener back.
/// </remarks>
public class ServiceStartupOrderTests
{
    /// <summary>
    /// Verifies that <c>GET /health</c> answers while the role pipeline is still starting.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Health_Answers_While_The_Role_Pipeline_Still_Starts()
    {
        var pipeline = new BlockingPipeline();
        var root = Path.Combine(Path.GetTempPath(), "rcynq-startup-order", Guid.NewGuid().ToString("N"));
        var port = ServiceTestHost.FreePort();
        var app = BuildHost(root, port, pipeline);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cancellationToken = lifetime.Token;

        try
        {
            var starting = app.StartAsync(cancellationToken);

            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/"),
                Timeout = TimeSpan.FromSeconds(5)
            };

            var status = await PollHealthAsync(client, TimeSpan.FromSeconds(15), cancellationToken);
            status.Should().Be(HttpStatusCode.OK, "the listener must be up before the role pipeline finishes");

            // The listener is bound a moment before the host reaches the pipeline, so the start is awaited, not sampled.
            var entered = async () => await pipeline.Entered.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            await entered.Should().NotThrowAsync("the role pipeline start must have been triggered");
            starting.IsCompleted.Should().BeFalse("the pipeline is still blocked, so the health answer came while it was starting");

            pipeline.Release();
            await starting;
        }
        finally
        {
            pipeline.Release();
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
            SqliteConnection.ClearAllPools();
            DeleteQuietly(root);
        }
    }

    /// <summary>
    /// Builds a Source service whose role pipeline blocks, with a temporary data root.
    /// </summary>
    /// <param name="root">The temporary root directory.</param>
    /// <param name="port">The loopback port.</param>
    /// <param name="pipeline">The pipeline the factory returns.</param>
    /// <returns>The built, not started application.</returns>
    private static WebApplication BuildHost(string root, int port, IRolePipeline pipeline)
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        var dataRoot = Path.Combine(root, "data");
        var settings = new Dictionary<string, string?>
        {
            ["SyncBridge:NodeId"] = "source-1",
            ["SyncBridge:Role"] = nameof(NodeRole.Source),
            ["SyncBridge:WorkdirPath"] = work,
            ["SyncBridge:DataRoot"] = dataRoot,
            ["SyncBridge:LinkToken"] = ServiceTestHost.LinkToken,
            ["SyncBridge:ListenPort"] = port.ToString(CultureInfo.InvariantCulture)
        };

        return ServiceHostBuilder.Build(
            new[] { "--config", Path.Combine(root, SyncBridgeOptions.DefaultFileName) },
            new ServiceHostOptions
            {
                RunBackgroundServices = true,
                ConfigureConfiguration = configuration => configuration.AddInMemoryCollection(settings),
                ConfigureBuilder = builder => builder.Services.AddSingleton<IRolePipelineFactory>(new StubPipelineFactory(pipeline))
            });
    }

    /// <summary>
    /// Calls <c>GET /health</c> until it answers or the budget is spent.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="budget">How long to keep trying.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The status received, or <see langword="null"/> when the endpoint never answered.</returns>
    private static async Task<HttpStatusCode?> PollHealthAsync(HttpClient client, TimeSpan budget, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(ApiRoutes.Health, cancellationToken);
                return response.StatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }

        return null;
    }

    /// <summary>
    /// Deletes a directory, ignoring failures.
    /// </summary>
    /// <param name="path">The directory path.</param>
    private static void DeleteQuietly(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// A factory that always returns the same pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    private sealed class StubPipelineFactory(IRolePipeline pipeline) : IRolePipelineFactory
    {
        /// <inheritdoc />
        public IRolePipeline Create(NodeRole role) => pipeline;
    }

    /// <summary>
    /// A pipeline whose start blocks until it is released, standing in for a long working-tree scan.
    /// </summary>
    private sealed class BlockingPipeline : IRolePipeline
    {
        /// <summary>Signals that the start may finish.</summary>
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Signals that <see cref="StartAsync"/> was entered.</summary>
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets a task that completes once <see cref="StartAsync"/> was entered.</summary>
        public Task Entered => _entered.Task;

        /// <inheritdoc />
        public NodeRole Role => NodeRole.Source;

        /// <inheritdoc />
        public RoleCapabilities Capabilities => RoleCapabilities.For(NodeRole.Source);

        /// <summary>
        /// Lets the blocked start finish.
        /// </summary>
        public void Release() => _release.TrySetResult();

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Release();
            return Task.CompletedTask;
        }
    }
}
