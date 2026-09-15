using CliFx.Exceptions;

namespace RCynQ.Cli.Hosting;

/// <summary>
/// Creates the node context of a command.
/// </summary>
public interface INodeContextFactory
{
    /// <summary>
    /// Loads and validates the configuration and builds the node services.
    /// </summary>
    /// <param name="configPath">The explicit configuration path, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The node context.</returns>
    /// <exception cref="CommandException">The configuration is missing or invalid.</exception>
    Task<NodeContext> CreateAsync(string? configPath, CancellationToken cancellationToken);
}
