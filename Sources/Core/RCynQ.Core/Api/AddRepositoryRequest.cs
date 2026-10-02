namespace RCynQ.Core.Api;

/// <summary>
/// The request to add a repository to the node.
/// </summary>
/// <param name="Name">The name the nodes call the repository by.</param>
/// <param name="Path">The local path: the Git repository on an Origin, the folder of the working copy on a Workstation.</param>
public sealed record AddRepositoryRequest(string Name, string Path);
