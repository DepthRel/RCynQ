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

    /// <summary>
    /// Initializes a new instance of the <see cref="ChainLinkConfigCheck"/> class.
    /// </summary>
    /// <param name="tokens">The link token provider.</param>
    public ChainLinkConfigCheck(LinkTokenProvider tokens)
        : base("ChainLinkConfig", AllRoles)
    {
        _tokens = tokens;
    }

    /// <inheritdoc />
    public override async Task<DiagnosticResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        var problems = SyncBridgeOptionsValidator.Validate(context.Options).ToList();
        var chain = context.Chain;
        if (context.Node.Role == NodeRole.Mediator)
        {
            if (chain.Count == 0)
            {
                problems.Add(Texts.Get("Diag.Chain.MediatorLinks"));
            }
        }
        else
        {
            if (chain.Count == 0)
            {
                problems.Add(Texts.Get("Diag.Chain.None"));
            }
            else
            {
                var counterpart = context.Node.Role == NodeRole.Source ? NodeRole.Dest : NodeRole.Source;
                var endpoint = chain[^1];
                if (endpoint.Peer.Role != counterpart)
                {
                    problems.Add(Texts.Format("Diag.Chain.LastLink", endpoint.Name, counterpart));
                }

                if (endpoint.Peer.SigningPublicKey is null)
                {
                    problems.Add(Texts.Format("Diag.Chain.NoSigningKey", endpoint.Peer.Id));
                }

                problems.AddRange(chain.Take(chain.Count - 1).Where(link => link.Peer.Role != NodeRole.Mediator)
                    .Select(link => Texts.Format("Diag.Chain.Intermediate", link.Name)));
            }

            var token = await _tokens.GetTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                problems.Add(Texts.Get("Diag.Chain.NoToken"));
            }
            else if (token.Length < LinkTokenProvider.MinimumLength)
            {
                problems.Add(Texts.Format("Diag.Chain.ShortToken", LinkTokenProvider.MinimumLength));
            }
        }

        return problems.Count == 0
            ? Pass($"{chain.Count} link(s) configured correctly.")
            : Fail(string.Join(" ", problems), Texts.Get("Diag.Chain.Fix"));
    }
}
