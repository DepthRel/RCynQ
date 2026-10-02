using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Enums;

namespace RCynQ.Service.Pipelines;

/// <summary>
/// <see cref="IRolePipelineFactory"/> that resolves the role pipelines from DI.
/// </summary>
public sealed class RolePipelineFactory : IRolePipelineFactory
{
    /// <summary>The service provider.</summary>
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="RolePipelineFactory"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    public RolePipelineFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public IRolePipeline Create(NodeRole role) => role switch
    {
        NodeRole.Workstation => _services.GetRequiredService<WorkstationPipeline>(),
        NodeRole.Relay => _services.GetRequiredService<RelayPipeline>(),
        NodeRole.Origin => _services.GetRequiredService<OriginPipeline>(),
        _ => throw new ArgumentException($"Node role '{role}' is not defined.", nameof(role))
    };
}
