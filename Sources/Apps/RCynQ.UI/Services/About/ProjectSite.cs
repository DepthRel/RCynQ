namespace RCynQ.UI.Services.About;

/// <summary>
/// The public addresses of the project: the page published with GitHub Pages (<c>Sources/Apps/RCynQ.Pages</c>), the
/// source code and the mail for problems and ideas.
/// </summary>
public static class ProjectSite
{
    /// <summary>
    /// The address of the project page, ending with a slash.
    /// </summary>
    public const string BASE_URL = "https://depthrel.github.io/RCynQ/";

    /// <summary>
    /// The address of the source code.
    /// </summary>
    public const string SOURCE_URL = "https://github.com/DepthRel/RCynQ";

    /// <summary>
    /// The address of the license text of RCynQ (GPL-3.0-or-later).
    /// </summary>
    public const string LICENSE_URL = SOURCE_URL + "/blob/main/LICENSE.txt";

    /// <summary>
    /// The address of the list of components of other authors and their licenses.
    /// </summary>
    public const string NOTICES_URL = SOURCE_URL + "/blob/main/THIRD-PARTY-NOTICES.txt";

    /// <summary>
    /// The mail address for problems, questions and ideas.
    /// </summary>
    public const string FEEDBACK_EMAIL = "project.rcynq@gmail.com";
}
