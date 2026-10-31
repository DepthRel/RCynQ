namespace RCynQ.MergeTools.Enums;

/// <summary>
/// How the installations of a merge tool are found.
/// </summary>
public enum MergeToolDiscovery
{
    /// <summary>By the names of its command in <c>PATH</c> and by the usual places of its installation.</summary>
    Paths,

    /// <summary>Through <c>vswhere</c>, which lists every installation of Visual Studio wherever it was installed.</summary>
    VisualStudio
}
