namespace RCynQ.MergeTools;

/// <summary>
/// One installation of Visual Studio as <c>vswhere</c> lists it.
/// </summary>
/// <param name="Name">The name of the product, such as "Visual Studio Community 2026".</param>
/// <param name="InstallationPath">The folder Visual Studio is installed in.</param>
public sealed record VisualStudioInstallation(string Name, string InstallationPath);
