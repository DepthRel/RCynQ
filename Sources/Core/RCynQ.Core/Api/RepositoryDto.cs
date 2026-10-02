namespace RCynQ.Core.Api;

/// <summary>
/// One repository as the local API shows it.
/// </summary>
/// <param name="Name">The name the nodes call the repository by; empty for a folder not bound to a repository of the Origin yet.</param>
/// <param name="Path">The local path, or <see langword="null"/> when the repository is not kept on this node yet.</param>
/// <param name="IsActive">Whether the node works with this repository now.</param>
/// <param name="IsLocal">Whether the node keeps the repository: always on an Origin, after the first download on a Workstation.</param>
/// <param name="IsOnOrigin">
/// Whether the Origin offers the repository. <see langword="null"/> when the Origin could not be asked.
/// </param>
public sealed record RepositoryDto(string Name, string? Path, bool IsActive, bool IsLocal, bool? IsOnOrigin);
