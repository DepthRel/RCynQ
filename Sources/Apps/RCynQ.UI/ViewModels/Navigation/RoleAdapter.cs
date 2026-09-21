using RCynQ.Core.Api;
using RCynQ.Core.Enums;

namespace RCynQ.UI.ViewModels.Navigation;

/// <summary>
/// Adapts the tabs to the node role: tabs that do not apply (synchronization and branches on a Mediator) are disabled.
/// </summary>
public sealed class RoleAdapter
{
    /// <summary>
    /// Checks whether a tab applies to a role.
    /// </summary>
    /// <param name="tab">The tab.</param>
    /// <param name="role">The role.</param>
    /// <returns><see langword="true"/> when the tab applies.</returns>
    public bool IsAvailable(TabKey tab, NodeRole role)
    {
        var capabilities = RoleCapabilities.For(role);
        return tab switch
        {
            TabKey.Sync => capabilities.ShowsSyncTab,
            TabKey.Branches => capabilities.ShowsBranchesTab,
            _ => true
        };
    }

    /// <summary>
    /// Applies the availability rules to navigation items and keeps an available tab selected.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <param name="role">The role.</param>
    /// <returns>The item that should be selected.</returns>
    public NavigationItemViewModel? Apply(IReadOnlyList<NavigationItemViewModel> items, NodeRole role)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            item.IsAvailable = IsAvailable(item.Key, role);
            item.UnavailableForRole = item.IsAvailable ? null : role;
        }

        return items.FirstOrDefault(item => item.IsSelected && item.IsAvailable) ?? items.FirstOrDefault(item => item.IsAvailable);
    }
}
