using System.Text.Json;

namespace RCynQ.Localization.Tests;

/// <summary>
/// Tests of <see cref="LocalizedError"/> and <see cref="LocalizerExtensions.Describe"/>: exceptions raised where nobody
/// has a localizer, worded later in the language of the reader.
/// </summary>
public sealed class LocalizedErrorTests
{
    /// <summary>
    /// Builds a localizer of a language of the application.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <returns>The localizer.</returns>
    private static Localizer In(string code) => new(LanguageCatalog.LoadEmbedded(), code);

    /// <summary>
    /// The message of the exception is the English text, whatever language anybody uses, and the reader gets the text in
    /// the reader's language.
    /// </summary>
    [Fact]
    public void Exception_Is_English_And_Is_Described_In_The_Language_Of_The_Reader()
    {
        var error = LocalizedError.Format("Keys.TypeUnsupported", "ssh-dss");

        Assert.Equal(In("en").Format("Keys.TypeUnsupported", "ssh-dss"), error.Message);
        Assert.Equal(In("en").Format("Keys.TypeUnsupported", "ssh-dss"), In("en").Describe(error));
        Assert.Equal(In("ru").Format("Keys.TypeUnsupported", "ssh-dss"), In("ru").Describe(error));
        Assert.NotEqual(In("en").Describe(error), In("ru").Describe(error));
    }

    /// <summary>
    /// A refused argument stays an <see cref="ArgumentException"/> with its parameter name; the description leaves the
    /// technical suffix of the message out.
    /// </summary>
    [Fact]
    public void Argument_Error_Keeps_Its_Type_And_Parameter()
    {
        var error = LocalizedError.Argument("value", "NodeId.TooLong", 64);

        Assert.IsType<ArgumentException>(error);
        Assert.Equal("value", error.ParamName);
        Assert.StartsWith(In("en").Format("NodeId.TooLong", 64), error.Message, StringComparison.Ordinal);
        Assert.Equal(In("ru").Format("NodeId.TooLong", 64), In("ru").Describe(error));
    }

    /// <summary>
    /// Any exception type can carry a text, with its inner exception.
    /// </summary>
    [Fact]
    public void Any_Exception_Type_Can_Carry_A_Text()
    {
        var cause = new InvalidOperationException("cause");

        var error = LocalizedError.Create(message => new JsonException(message, cause), "NodeId.NotString");

        Assert.IsType<JsonException>(error);
        Assert.Same(cause, error.InnerException);
        Assert.Equal(In("ru").Get("NodeId.NotString"), In("ru").Describe(error));
        Assert.True(LocalizedError.TryRead(error, out var key, out var args));
        Assert.Equal("NodeId.NotString", key);
        Assert.Empty(args);
    }

    /// <summary>
    /// A cause passed as a value of the text is worded in the language of the reader too, and stands as its English
    /// message in the message of the exception.
    /// </summary>
    [Fact]
    public void Cause_Among_The_Values_Is_Described_Too()
    {
        var cause = LocalizedError.Format("Keys.BlobShort");

        var error = LocalizedError.Create(message => new FormatException(message, cause), "Transfer.FileInvalid", cause);

        Assert.Equal(In("en").Format("Transfer.FileInvalid", In("en").Get("Keys.BlobShort")), error.Message);
        Assert.Equal(In("ru").Format("Transfer.FileInvalid", In("ru").Get("Keys.BlobShort")), In("ru").Describe(error));
    }

    /// <summary>
    /// An exception that carries no text is described by its own message, and is not mistaken for one that does.
    /// </summary>
    [Fact]
    public void Plain_Exception_Is_Described_By_Its_Message()
    {
        var plain = new InvalidOperationException("as it is");

        Assert.Equal("as it is", In("ru").Describe(plain));
        Assert.False(LocalizedError.TryRead(plain, out _, out _));
        Assert.False(LocalizedError.TryRead(null, out _, out _));
    }
}
