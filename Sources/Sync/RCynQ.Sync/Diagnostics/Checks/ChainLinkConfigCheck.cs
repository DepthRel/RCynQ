using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Sync.Security;
using RCynQ.Localization;

namespace RCynQ.Sync.Diagnostics.Checks;

/// <summary>
/// Checks the configuration and the stored chain: topology, peer signing keys and the link token.
/// </summary>
public sealed class ChainLinkConfigCheck : DiagnosticCheckBase
{
    /// <summary>The link token provider.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChainLinkConfigCheck"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="tokens">The link token provider.</param>
    public ChainLinkConfigCheck(ILocalizer localizer, LinkTokenProvider tokens)
        : base("ChainLinkConfig", AllRoles)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _tokens = tokens;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var problems = SyncBridgeOptionsValidator.Validate(context.Options, _localizer).ToList();
        var chain = context.Chain;
        if (context.Node.Role == NodeRole.Mediator)
        {
            if (chain.Count == 0)
            {
                problems.Add(_localizer.Get("Diag.Chain.MediatorLinks"));
            }
        }
        else
        {
            if (chain.Count == 0)
            {
                problems.Add(_localizer.Get("Diag.Chain.None"));
            }
            else
            {
                var counterpart = context.Node.Role == NodeRole.Source ? NodeRole.Dest : NodeRole.Source;
                var endpoint = chain[^1];
                if (endpoint.Peer.Role != counterpart)
                {
                    problems.Add(_localizer.Format("Diag.Chain.LastLink", endpoint.Name, counterpart));
                }

                if (endpoint.Peer.SigningPublicKey is null)
                {
                    problems.Add(_localizer.Format("Diag.Chain.NoSigningKey", endpoint.Peer.Id));
                }

                problems.AddRange(chain.Take(chain.Count - 1).Where(link => link.Peer.Role != NodeRole.Mediator)
                    .Select(link => _localizer.Format("Diag.Chain.Intermediate", link.Name)));
            }

            var token = await _tokens.GetTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                problems.Add(_localizer.Get("Diag.Chain.NoToken"));
            }
            else if (token.Length < LinkTokenProvider.MinimumLength)
            {
                problems.Add(_localizer.Format("Diag.Chain.ShortToken", LinkTokenProvider.MinimumLength));
            }
        }

        return problems.Count == 0
            ? Pass(_localizer.Format("Diag.Chain.Ok", chain.Count))
            : Fail(string.Join(" ", problems), _localizer.Get("Diag.Chain.Fix"));
    }
}
