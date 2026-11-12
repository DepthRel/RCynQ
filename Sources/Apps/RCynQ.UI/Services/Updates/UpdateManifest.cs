using System.Globalization;
using System.Text.Json;

namespace RCynQ.UI.Services.Updates;

/// <summary>
/// Reads the version of the latest release from <c>updates.json</c> of the project page, which came over the network
/// and is therefore read as untrusted: only the version of the own operating system is taken, the links in the file never.
/// </summary>
public static class UpdateManifest
{
    /// <summary>
    /// The longest number of a part of a version.
    /// </summary>
    private const int MAX_PART_LENGTH = 5;

    /// <summary>
    /// Gets the name of the section of <c>updates.json</c> for the operating system the application runs on.
    /// </summary>
    /// <returns><c>windows</c>, <c>linux</c> or <c>macos</c>; <see langword="null"/> where no package is published.</returns>
    public static string? CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return OperatingSystem.IsMacOS() ? "macos" : null;
    }

    /// <summary>
    /// Reads the version of the latest release for a platform.
    /// </summary>
    /// <param name="json">The content of <c>updates.json</c>.</param>
    /// <param name="platform">The section of the platform.</param>
    /// <returns>The version, or <see langword="null"/> when the file is not a list of releases or names no valid version.</returns>
    public static Version? TryReadVersion(string json, string platform)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(platform, out var section) ||
                section.ValueKind != JsonValueKind.Object ||
                !section.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return TryParse(version.GetString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a version of three numbers, as releases are tagged: <c>0.3.0</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The version, or <see langword="null"/> for anything else.</returns>
    public static Version? TryParse(string? text)
    {
        var parts = text?.Split('.');
        if (parts is not { Length: 3 } || parts.Any(part => part.Length is 0 or > MAX_PART_LENGTH || !part.All(char.IsAsciiDigit)))
        {
            return null;
        }

        var numbers = parts.Select(part => int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
        return new Version(numbers[0], numbers[1], numbers[2]);
    }
}
