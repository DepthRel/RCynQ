using RCynQ.Core.Enums;

namespace RCynQ.Core.Config.Legacy;

/// <summary>
/// Translates the role names of earlier versions into <see cref="NodeRole"/>.
/// </summary>
/// <remarks>
/// Earlier versions called the roles <c>Source</c>, <c>Mediator</c> and <c>Dest</c>. Configuration files and export files
/// written by them still carry those names. This class is the only place that knows them.
/// </remarks>
public static class LegacyRoleNames
{
    /// <summary>
    /// The earlier names and the roles they stand for.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, NodeRole> _roles = new Dictionary<string, NodeRole>(StringComparer.OrdinalIgnoreCase)
    {
        ["Source"] = NodeRole.Workstation,
        ["Mediator"] = NodeRole.Relay,
        ["Dest"] = NodeRole.Origin
    };

    /// <summary>
    /// Translates a role name of an earlier version.
    /// </summary>
    /// <param name="name">The name to translate; may be <see langword="null"/>.</param>
    /// <param name="role">The role the name stands for.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is a role name of an earlier version.</returns>
    public static bool TryTranslate(string? name, out NodeRole role)
    {
        role = default;
        return name is not null && _roles.TryGetValue(name.Trim(), out role);
    }
}
