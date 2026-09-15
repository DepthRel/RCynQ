using RCynQ.Core.Enums;

namespace RCynQ.Service.Pipelines;

/// <summary>
/// Creates the pipeline of a role.
/// </summary>
public interface IRolePipelineFactory
{
    /// <summary>
    /// Creates the pipeline for a role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="ArgumentException"><paramref name="role"/> is not defined.</exception>
    IRolePipeline Create(NodeRole role);
}
