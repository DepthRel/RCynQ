using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Transports.Abstractions.Links;
using RCynQ.Localization;

namespace RCynQ.Transports.Abstractions.Factory;

/// <summary>
/// <see cref="ITransportLinkFactory"/> that delegates to the registered <see cref="ITransportLinkProvider"/> instances.
/// </summary>
public sealed class TransportLinkFactory : ITransportLinkFactory
{
    /// <summary>
    /// The providers by kind.
    /// </summary>
    private readonly IReadOnlyDictionary<TransportKind, ITransportLinkProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportLinkFactory"/> class.
    /// </summary>
    /// <param name="providers">The registered providers; the last registration of a kind wins.</param>
    public TransportLinkFactory(IEnumerable<ITransportLinkProvider> providers)
    {
        var map = new Dictionary<TransportKind, ITransportLinkProvider>();
        foreach (var provider in providers)
        {
            map[provider.Kind] = provider;
        }

        _providers = map;
    }

    /// <summary>
    /// Gets the kinds with a registered provider.
    /// </summary>
    public IReadOnlyCollection<TransportKind> SupportedKinds => _providers.Keys.ToArray();

    /// <summary>
    /// Checks whether a kind has a registered provider.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns><see langword="true"/> when supported.</returns>
    public bool IsSupported(TransportKind kind) => _providers.ContainsKey(kind);

    /// <inheritdoc />
    public ITransportLink Create(ChainLinkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!_providers.TryGetValue(config.Transport, out var provider))
        {
            throw new ArgumentException(
                Texts.Format("Transport.NotAvailableList", config.Transport, string.Join(", ", _providers.Keys)),
                nameof(config));
        }

        return provider.Create(config);
    }
}
