namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// How to start RCynQ.Service.
/// </summary>
/// <param name="FileName">The executable: the service apphost or the <c>dotnet</c> host.</param>
/// <param name="PrefixArguments">Arguments placed before the service arguments (the service assembly for <c>dotnet</c>).</param>
public sealed record ServiceLaunchCommand(string FileName, IReadOnlyList<string> PrefixArguments);
