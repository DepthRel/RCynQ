using System.Text;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;

namespace RCynQ.Cli.Services;

/// <summary>
/// Reads, validates and edits <c>syncbridge.json</c>. Works without a valid node configuration.
/// </summary>
public sealed class ConfigCommandService
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    public ConfigCommandService(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>
    /// Validates a configuration file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The problems; empty when the file is valid.</returns>
    public async Task<IReadOnlyList<string>> ValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return SyncBridgeOptionsValidator.Validate(await ConfigurationFileEditor.LoadAsync(path, cancellationToken), _localizer);
        }
        catch (SyncBridgeException ex)
        {
            return new[] { _localizer.Describe(ex) };
        }
    }

    /// <summary>
    /// Sets the role and paths, creating the file with a node identifier when necessary.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="role">The role.</param>
    /// <param name="repoPath">The repository path, or <see langword="null"/>.</param>
    /// <param name="workdirPath">The working directory, or <see langword="null"/>.</param>
    /// <param name="nodeId">The node identifier to set, or <see langword="null"/> to keep or derive one.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A summary.</returns>
    /// <exception cref="ArgumentException">The node identifier or a path is invalid for the role.</exception>
    public async Task<string> SetRoleAsync(string path, NodeRole role, string? repoPath, string? workdirPath, string? nodeId, CancellationToken cancellationToken = default)
    {
        if (nodeId is not null)
        {
            NodeId.Parse(nodeId);
        }

        SyncBridgeOptions? existing = null;
        if (File.Exists(path))
        {
            existing = await ConfigurationFileEditor.LoadAsync(path, cancellationToken);
        }

        var effectiveId = nodeId ?? (string.IsNullOrWhiteSpace(existing?.NodeId) ? DeriveNodeId(role) : existing.NodeId);
        var existingPath = RepositoryConfigurationEditor.ActivePath(existing);
        var repo = role == NodeRole.Origin ? repoPath ?? existingPath : null;
        var workdir = role == NodeRole.Workstation ? workdirPath ?? existingPath : null;
        _ = new NodeConfig(NodeId.Parse(effectiveId), effectiveId, role, repo, workdir);

        await ConfigurationFileEditor.UpdateAsync(path, section => ConfigurationFileEditor.SetProperty(section, nameof(SyncBridgeOptions.NodeId), effectiveId), cancellationToken);
        await ConfigurationFileEditor.SetRoleAsync(path, role, repoPath, workdirPath, cancellationToken);

        var text = new StringBuilder();
        text.Append("Saved ").Append(path).Append('\n');
        text.Append("  Node:    ").Append(effectiveId).Append(" (").Append(role).Append(")\n");
        if (repo is not null)
        {
            text.Append("  Repo:    ").Append(repo).Append('\n');
        }

        if (workdir is not null)
        {
            text.Append("  Workdir: ").Append(workdir).Append('\n');
        }

        text.Append("Restart RCynQ.Service to apply the role.\n");
        return text.ToString();
    }

    /// <summary>
    /// Derives a node identifier from the machine name and role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The identifier.</returns>
    public static string DeriveNodeId(NodeRole role)
    {
        var machine = new string(Environment.MachineName.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return $"{(machine.Length == 0 ? "node" : machine)}-{role.ToString().ToLowerInvariant()}";
    }
}
