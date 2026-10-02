using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// The repositories of the node.
/// </summary>
/// <param name="Role">The role of the node.</param>
/// <param name="Active">The name of the repository the node works with, or <see langword="null"/>.</param>
/// <param name="PeerActive">The name of the repository the node at the other end works with, as far as known.</param>
/// <param name="Repositories">The repositories: those kept on the node and, on a Workstation, those the Origin offers.</param>
/// <param name="CatalogError">Why the Origin could not be asked for what it offers, or <see langword="null"/>.</param>
public sealed record RepositoryListDto(NodeRole Role, string? Active, string? PeerActive, IReadOnlyList<RepositoryDto> Repositories, string? CatalogError);
