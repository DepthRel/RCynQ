using RCynQ.Localization;

namespace RCynQ.UI.Services.About;

/// <summary>
/// Writes the letter about a problem: a <c>mailto:</c> address that opens the mail program of the user with the
/// subject, the questions to answer and the facts about the application already filled in. Nothing is sent until the
/// user sends the letter.
/// </summary>
public static class ProblemReport
{
    /// <summary>
    /// Writes the letter about a problem in the language of the interface.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="version">The version of the application.</param>
    /// <param name="system">The operating system and the processor architecture.</param>
    /// <returns>The <c>mailto:</c> address of the letter to <see cref="ProjectSite.FEEDBACK_EMAIL"/>.</returns>
    public static Uri CreateMailto(ILocalizer localizer, string version, string system)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        var subject = localizer.Format("Problem.Subject", version);
        var body = localizer.Format("Problem.Body", localizer.Get("Nav.Logs"), localizer.Get("Logs.Export")) +
            "\n\n---\n" +
            localizer.Format("Problem.Facts", version, system, localizer.Current.Code);

        var query = "subject=" + Escape(subject) + "&body=" + Escape(body);

        return new Uri($"mailto:{ProjectSite.FEEDBACK_EMAIL}?{query}");
    }

    /// <summary>
    /// Escapes a part of a <c>mailto:</c> address; line breaks become CRLF, as RFC 6068 asks.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    private static string Escape(string text)
    {
        return Uri.EscapeDataString(text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
    }
}
