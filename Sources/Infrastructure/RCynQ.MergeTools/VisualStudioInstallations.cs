using System.Text.Json;

namespace RCynQ.MergeTools;

/// <summary>
/// Reads the list of Visual Studio installations <c>vswhere -format json</c> prints.
/// </summary>
public static class VisualStudioInstallations
{
    /// <summary>
    /// The place of the merge tool of Visual Studio inside an installation.
    /// </summary>
    public const string MERGE_TOOL_PATH = @"Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\vsDiffMerge.exe";

    /// <summary>
    /// Reads the installations.
    /// </summary>
    /// <param name="json">The output of <c>vswhere</c>.</param>
    /// <returns>The installations with a name and a folder; nothing when the output cannot be read.</returns>
    public static IReadOnlyList<VisualStudioInstallation> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<VisualStudioInstallation>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<VisualStudioInstallation>();
            }

            var installations = new List<VisualStudioInstallation>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var path = Text(item, "installationPath");
                if (path is null)
                {
                    continue;
                }

                installations.Add(new VisualStudioInstallation(Text(item, "displayName") ?? "Visual Studio", path));
            }

            return installations;
        }
        catch (JsonException)
        {
            return Array.Empty<VisualStudioInstallation>();
        }
    }

    /// <summary>
    /// Reads a text property of an object.
    /// </summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The name of the property.</param>
    /// <returns>The text, or <see langword="null"/> when it is missing or blank.</returns>
    private static string? Text(JsonElement item, string name)
        => item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;
}
