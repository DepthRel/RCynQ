using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

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
                problems.Add("A Mediator should list the neighbour links whose transports it maintains.");
            }
        }
        else
        {
            if (chain.Count == 0)
            {
                problems.Add("No chain links are configured.");
            }
            else
            {
                var counterpart = context.Node.Role == NodeRole.Source ? NodeRole.Dest : NodeRole.Source;
                var endpoint = chain[^1];
                if (endpoint.Peer.Role != counterpart)
                {
                    problems.Add($"The last link '{endpoint.Name}' must point to a {counterpart} peer.");
                }

                if (endpoint.Peer.SigningPublicKey is null)
                {
                    problems.Add($"Peer {endpoint.Peer.Id} has no signing public key; its patches cannot be verified. Import its exported configuration.");
                }

                problems.AddRange(chain.Take(chain.Count - 1).Where(link => link.Peer.Role != NodeRole.Mediator)
                    .Select(link => $"Intermediate link '{link.Name}' must point to a Mediator."));
            }

            var token = await _tokens.GetTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(token))
            {
                problems.Add("The link token is not configured.");
            }
            else if (token.Length < LinkTokenProvider.MinimumLength)
            {
                problems.Add($"The link token is shorter than {LinkTokenProvider.MinimumLength} characters.");
            }
        }

        return problems.Count == 0
            ? Pass($"{chain.Count} link(s) configured correctly.")
            : Fail(string.Join(" ", problems), "Fix syncbridge.json or the stored links, then run diagnostics again.");
    }
}
