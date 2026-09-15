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
        NodeRole.Source => _services.GetRequiredService<SourcePipeline>(),
        NodeRole.Mediator => _services.GetRequiredService<MediatorPipeline>(),
        NodeRole.Dest => _services.GetRequiredService<DestPipeline>(),
        _ => throw new ArgumentException($"Node role '{role}' is not defined.", nameof(role))
    };
}
