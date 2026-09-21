using FluentAssertions;
using RCynQ.UI.Services.Configuration;

namespace RCynQ.UI.Tests.Localization;

/// <summary>
/// Tests of <see cref="JsonUiSettingsStore"/>: the local file the language choice is kept in.
/// </summary>
public sealed class JsonUiSettingsStoreTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-ui-settings-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that the settings file lives next to the configuration file.
    /// </summary>
    [Fact]
    public void PathFor_Uses_The_Directory_Of_The_Configuration()
        => JsonUiSettingsStore.PathFor(Path.Combine(_root, "config", "syncbridge.json"))
            .Should().Be(Path.Combine(_root, "config", "ui-settings.json"));

    /// <summary>
    /// Verifies that a value survives a new instance, and that the directory is created on the first write.
    /// </summary>
    [Fact]
    public void Set_Then_Get_Persists_Across_Instances()
    {
        var path = Path.Combine(_root, "config", "ui-settings.json");

        new JsonUiSettingsStore(path).Set("language", "ru");

        new JsonUiSettingsStore(path).Get("language").Should().Be("ru");
    }

    /// <summary>
    /// Verifies that writing one setting keeps the others.
    /// </summary>
    [Fact]
    public void Set_Keeps_Other_Settings()
    {
        var store = new JsonUiSettingsStore(Path.Combine(_root, "ui-settings.json"));
        store.Set("a", "1");

        store.Set("b", "2");

        store.Get("a").Should().Be("1");
        store.Get("b").Should().Be("2");
    }

    /// <summary>
    /// Verifies that a missing or damaged file reads as empty and is replaced by the next write.
    /// </summary>
    [Fact]
    public void Damaged_File_Reads_As_Empty_And_Is_Replaced()
    {
        var path = Path.Combine(_root, "ui-settings.json");
        File.WriteAllText(path, "{ not json");
        var store = new JsonUiSettingsStore(path);

        store.Get("language").Should().BeNull();
        store.Set("language", "en");

        store.Get("language").Should().Be("en");
        new JsonUiSettingsStore(Path.Combine(_root, "none.json")).Get("language").Should().BeNull();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
