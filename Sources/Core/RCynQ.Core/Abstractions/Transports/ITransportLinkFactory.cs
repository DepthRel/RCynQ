using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Abstractions.Transports;

/// <summary>
/// Creates transport links from link configurations.
/// </summary>
public interface ITransportLinkFactory
{
    /// <summary>
    /// Creates a transport link for the given configuration.
    /// </summary>
    /// <param name="config">
    /// The link configuration. Must not be <see langword="null"/> and must describe
    /// a supported <see cref="TransportKind"/>.
    /// </param>
    /// <returns>A new, not yet started, transport link.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The configured transport kind is not supported by this factory.
    /// </exception>
    ITransportLink Create(ChainLinkConfig config);
}
