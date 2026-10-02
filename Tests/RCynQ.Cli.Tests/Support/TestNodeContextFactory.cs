using Microsoft.Extensions.DependencyInjection;
using RCynQ.Cli.Hosting;
using RCynQ.Core.Config;

namespace RCynQ.Cli.Tests.Support;

/// <summary>
/// <see cref="INodeContextFactory"/> that builds node contexts from test registrations instead of a configuration file.
/// </summary>
public sealed class TestNodeContextFactory : INodeContextFactory
{
    /// <summary>The test registrations.</summary>
    private readonly Action<IServiceCollection> _configure;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestNodeContextFactory"/> class.
    /// </summary>
    /// <param name="configure">Registers the mocked node services.</param>
    public TestNodeContextFactory(Action<IServiceCollection> configure)
    {
        _configure = configure;
    }

    /// <summary>
    /// Gets the number of created contexts.
    /// </summary>
    public int Created { get; private set; }

    /// <inheritdoc />
    public Task<NodeContext> CreateAsync(string? configPath, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        _configure(services);
        services.AddSingleton(TestTexts.English);
        services.AddCliCommandServices();
        Created++;
        return Task.FromResult(new NodeContext(services.BuildServiceProvider(), new SyncBridgeOptions(), configPath ?? "test.json"));
    }
}
