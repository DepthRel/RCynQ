using RCynQ.Core.Api;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Repositories;

/// <summary>
/// One repository in the switcher and in the list of repositories.
/// </summary>
public sealed class RepositoryItemViewModel
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryItemViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="repository">The repository.</param>
    public RepositoryItemViewModel(ILocalizer localizer, RepositoryDto repository)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>Gets the repository.</summary>
    public RepositoryDto Repository { get; }

    /// <summary>Gets the name the nodes call the repository by; empty for a folder that has no name yet.</summary>
    public string Name => Repository.Name;

    /// <summary>Gets the name shown to the user.</summary>
    public string DisplayName => Name.Length > 0 ? Name : _localizer.Get("Repos.Unnamed");

    /// <summary>Gets the local folder, or the note that the repository is not on this computer yet.</summary>
    public string Location => Repository.Path ?? _localizer.Get("Repos.NotDownloaded");

    /// <summary>Gets a value indicating whether the node works with this repository now.</summary>
    public bool IsActive => Repository.IsActive;

    /// <summary>Gets a value indicating whether the node keeps the repository.</summary>
    public bool IsLocal => Repository.IsLocal;

    /// <summary>Gets a value indicating whether the node can switch to the repository: it has a name and is not the current one.</summary>
    public bool CanSwitch => !IsActive && Name.Length > 0;

    /// <summary>Gets a value indicating whether the repository can be removed from the node: it is kept here and is not the current one.</summary>
    public bool CanRemove => IsLocal && !IsActive && Name.Length > 0;

    /// <summary>Gets the note about the state of the repository, or an empty string.</summary>
    public string Note
    {
        get
        {
            if (IsActive)
            {
                return _localizer.Get("Repos.Current");
            }

            return Repository.IsOnOrigin == false ? _localizer.Get("Repos.NotOnOrigin") : string.Empty;
        }
    }

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}
