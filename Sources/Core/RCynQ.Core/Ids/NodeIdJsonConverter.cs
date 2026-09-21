using System.Text.Json;
using System.Text.Json.Serialization;
using RCynQ.Localization;

namespace RCynQ.Core.Ids;

/// <summary>
/// Serializes <see cref="NodeId"/> as a JSON string.
/// </summary>
public sealed class NodeIdJsonConverter : JsonConverter<NodeId>
{
    /// <summary>
    /// Reads a <see cref="NodeId"/> from a JSON string token.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The target type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The parsed node identifier.</returns>
    /// <exception cref="JsonException">The token is not a valid node identifier string.</exception>
    public override NodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(Texts.Get("NodeId.NotString"));
        }

        var text = reader.GetString();
        if (!NodeId.TryParse(text, out var nodeId))
        {
            throw new JsonException(Texts.Format("NodeId.NotValid", text));
        }

        return nodeId;
    }

    /// <summary>
    /// Writes a <see cref="NodeId"/> as a JSON string.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The node identifier.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, NodeId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
