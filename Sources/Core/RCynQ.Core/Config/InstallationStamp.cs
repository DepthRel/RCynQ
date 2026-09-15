using System.Text.Json;
using System.Text.Json.Nodes;

namespace RCynQ.Core.Config;

/// <summary>
/// Records on which computer the data directory of a node was created, to detect an installation copied together with
/// the identity and keys of another node.
/// </summary>
public static class InstallationStamp
{
    /// <summary>
    /// The stamp file inside the data directory.
    /// </summary>
    public const string FileName = "installation.json";

    /// <summary>
    /// Compares the stamp with the current computer and creates the stamp when the data directory has none.
    /// </summary>
    /// <param name="dataRoot">The data directory.</param>
    /// <param name="machineName">The current computer name.</param>
    /// <returns>The result; a missing or unreadable stamp is never reported as a copy.</returns>
    public static InstallationCheck Check(string dataRoot, string machineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        var path = Path.Combine(dataRoot, FileName);
        try
        {
            if (File.Exists(path))
            {
                var recorded = JsonNode.Parse(File.ReadAllText(path))?["machineName"]?.GetValue<string>();
                var copied = !string.IsNullOrWhiteSpace(recorded) && !string.Equals(recorded, machineName, StringComparison.OrdinalIgnoreCase);
                return new InstallationCheck(copied, recorded);
            }

            if (!Directory.Exists(dataRoot))
            {
                return new InstallationCheck(false, null);
            }

            Write(dataRoot, machineName);
            return new InstallationCheck(false, machineName);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return new InstallationCheck(false, null);
        }
    }

    /// <summary>
    /// Writes the stamp for the current computer, for example after the user confirmed that the node was moved.
    /// </summary>
    /// <param name="dataRoot">The data directory.</param>
    /// <param name="machineName">The current computer name.</param>
    public static void Write(string dataRoot, string machineName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        Directory.CreateDirectory(dataRoot);
        var stamp = new JsonObject
        {
            ["machineName"] = machineName,
            ["recordedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };
        File.WriteAllText(Path.Combine(dataRoot, FileName), stamp.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
