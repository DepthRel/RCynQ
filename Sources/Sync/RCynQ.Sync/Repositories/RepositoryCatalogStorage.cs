namespace RCynQ.Sync.Repositories;

/// <summary>
/// Tells the <see cref="RepositoryCatalog"/> where to keep its entries.
/// </summary>
/// <param name="ConfigFilePath">
/// The configuration file of the node, or <see langword="null"/> to keep the entries in memory only (tools and tests
/// that have no file).
/// </param>
public sealed record RepositoryCatalogStorage(string? ConfigFilePath);
