using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Transports.Abstractions.Links;

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
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportLinkFactory"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="providers">The registered providers; the last registration of a kind wins.</param>
    public TransportLinkFactory(ILocalizer localizer, IEnumerable<ITransportLinkProvider> providers)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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
                _localizer.Format("Transport.NotAvailableList", config.Transport, string.Join(", ", _providers.Keys)),
                nameof(config));
        }

        return provider.Create(config);
    }
}
