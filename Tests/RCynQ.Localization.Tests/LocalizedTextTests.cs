namespace RCynQ.Localization.Tests;

/// <summary>
/// Tests of <see cref="LocalizedText"/>: messages stored as a key and values and worded when they are shown.
/// </summary>
public sealed class LocalizedTextTests
{
    /// <summary>
    /// Builds a localizer of a language of the application.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <returns>The localizer.</returns>
    private static Localizer In(string code) => new(LanguageCatalog.LoadEmbedded(), code);

    /// <summary>
    /// The same stored message is worded in whichever language it is shown in.
    /// </summary>
    [Fact]
    public void Encoded_Message_Follows_The_Language_Of_The_Reader()
    {
        var stored = LocalizedText.Encode("Patch.Sent", 3, "dest-1");

        Assert.Equal("Sent 3 file(s) to dest-1.", LocalizedText.Render(stored, In("en")));
        Assert.Equal("Отправлено файлов: 3, получатель: dest-1.", LocalizedText.Render(stored, In("ru")));
    }

    /// <summary>
    /// A message without values, and a stored value that contains the characters of a format string, work.
    /// </summary>
    [Fact]
    public void Message_Without_Values_And_Values_With_Braces_Work()
    {
        Assert.Equal("Modified locally since the last sync.", LocalizedText.Render(LocalizedText.Encode("Conflict.ModifiedLocally"), In("en")));
        Assert.Equal("Rejected hunks were written to a{0}b.rej.", LocalizedText.Render(LocalizedText.Encode("Apply.RejectedHunks", "a{0}b"), In("en")));
    }

    /// <summary>
    /// A plain text (from an earlier version, or from the system) and <see langword="null"/> are shown as they are.
    /// </summary>
    [Fact]
    public void Plain_Text_Passes_Through()
    {
        Assert.False(LocalizedText.IsEncoded("Already a sentence."));
        Assert.Equal("Already a sentence.", LocalizedText.Render("Already a sentence.", In("ru")));
        Assert.Null(LocalizedText.Render(null));
    }

    /// <summary>
    /// A key that no language has shows the key, so a gap is visible and never breaks the screen.
    /// </summary>
    [Fact]
    public void Unknown_Key_Shows_The_Key()
        => Assert.Equal("No.Such.Key", LocalizedText.Render(LocalizedText.Encode("No.Such.Key"), In("en")));

    /// <summary>
    /// The separator inside a value cannot forge another value.
    /// </summary>
    [Fact]
    public void Separator_In_A_Value_Is_Neutralized()
    {
        var stored = LocalizedText.Encode("Patch.Sent", 1, "a\u001Fb");

        Assert.Equal("Sent 1 file(s) to a b.", LocalizedText.Render(stored, In("en")));
    }
}
