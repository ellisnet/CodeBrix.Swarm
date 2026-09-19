using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Swarm.Core.Configuration;

/// <summary>
/// Carries a JSON value through as the text it already is. The part of a Worker's configuration that
/// belongs to the consuming application is written by one process and read by another, and the
/// swarm is not allowed to look inside it, so it is neither parsed into a model nor re-encoded as a
/// string: this converter copies the raw JSON out on the way in and writes it back verbatim on the
/// way out.
/// </summary>
internal sealed class RawJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (string.IsNullOrWhiteSpace(value))
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteRawValue(value);
    }
}
