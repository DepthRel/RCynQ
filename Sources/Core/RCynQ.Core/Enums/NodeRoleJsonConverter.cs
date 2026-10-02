using System.Text.Json;
using System.Text.Json.Serialization;
using RCynQ.Core.Config.Legacy;

namespace RCynQ.Core.Enums;

/// <summary>
/// Serializes <see cref="NodeRole"/> as its name and reads the names of earlier versions too.
/// </summary>
/// <remarks>
/// A role is read from its current name (in any letter case), from the name an earlier version gave it
/// (see <see cref="LegacyRoleNames"/>) or from its number. It is always written as its current name.
/// </remarks>
public sealed class NodeRoleJsonConverter : JsonConverter<NodeRole>
{
    /// <summary>
    /// Reads a <see cref="NodeRole"/> from a JSON string or number token.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The target type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The role.</returns>
    /// <exception cref="JsonException">The token does not name a role.</exception>
    public override NodeRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined((NodeRole)number))
        {
            return (NodeRole)number;
        }

        var roles = string.Join(", ", Enum.GetNames<NodeRole>());
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A node role must be a string. The roles are {roles}.");
        }

        var text = reader.GetString();
        return TryParse(text, out var role)
            ? role
            : throw new JsonException($"\"{text}\" is not a node role. The roles are {roles}.");
    }

    /// <summary>
    /// Writes a <see cref="NodeRole"/> as its name.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The role.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, NodeRole value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());

    /// <summary>
    /// Parses a role from its current name or from the name an earlier version gave it.
    /// </summary>
    /// <param name="text">The name; may be <see langword="null"/>.</param>
    /// <param name="role">The role.</param>
    /// <returns><see langword="true"/> when <paramref name="text"/> names a role.</returns>
    public static bool TryParse(string? text, out NodeRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var name = text.Trim();

        // Enum.TryParse also accepts numbers and lists of names, which are not role names.
        foreach (var candidate in Enum.GetValues<NodeRole>())
        {
            if (string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                role = candidate;
                return true;
            }
        }

        return LegacyRoleNames.TryTranslate(name, out role);
    }
}
