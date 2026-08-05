using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentationGenerator.Domain.Models;

namespace DocumentationGenerator.Infrastructure.Ollama;

public static class OllamaJsonParser
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new FlexibleStringConverter());
        options.Converters.Add(new FlexibleStringListConverter());
        return options;
    }

    public static T Deserialize<T>(string response) where T : class
    {
        var json = ExtractObject(response);
        return JsonSerializer.Deserialize<T>(json, Options)
               ?? throw new JsonException("The response contained JSON null.");
    }

    public static string ExtractObject(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) throw new JsonException("The response was empty.");
        var cleaned = response.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.Ordinal).Trim();
        var start = cleaned.IndexOf('{');
        if (start < 0) throw new JsonException("No JSON object was found.");

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < cleaned.Length; index++)
        {
            var current = cleaned[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') inString = false;
                continue;
            }
            if (current == '"') inString = true;
            else if (current == '{') depth++;
            else if (current == '}' && --depth == 0) return cleaned[start..(index + 1)];
        }
        throw new JsonException("The JSON object was incomplete.");
    }

    public static string SanitizePreview(string response)
    {
        var value = new string(response.Where(character => !char.IsControl(character) || character == ' ').ToArray());
        return value.Length <= 240 ? value : value[..240] + "...";
    }

    private static string ReadableText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.Array => string.Join("; ", element.EnumerateArray()
                .Select(ReadableText).Where(value => !string.IsNullOrWhiteSpace(value))),
            JsonValueKind.Object => ReadObject(element),
            _ => element.GetRawText()
        };
    }

    private static string ReadObject(JsonElement element)
    {
        var properties = element.EnumerateObject().ToList();
        if (properties.Count == 1) return ReadableText(properties[0].Value);
        return string.Join("; ", properties.Select(property =>
        {
            var value = ReadableText(property.Value);
            return string.IsNullOrWhiteSpace(value) ? property.Name : $"{property.Name}: {value}";
        }));
    }

    private sealed class FlexibleStringConverter : JsonConverter<string>
    {
        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return ReadableText(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    private sealed class FlexibleStringListConverter : JsonConverter<List<string>>
    {
        public override bool HandleNull => true;

        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return [];

            var elements = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToList()
                : [document.RootElement.Clone()];
            return elements.Select(ReadableText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value) writer.WriteStringValue(item);
            writer.WriteEndArray();
        }
    }
}
