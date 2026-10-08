using System.Text.Json;
using System.Text.Json.Nodes;

namespace PteFloatingSentence.Windows.Infrastructure;

/// <summary>Turns the Gemini-style schemas used in this app into strict standard JSON Schema.</summary>
public static class JsonSchemaConverter
{
    /// <summary>
    /// Lower-cases type names, sets <c>additionalProperties: false</c> on every object and lists every property
    /// as required, which is what strict structured-output modes (OpenAI-compatible, Anthropic) expect.
    /// </summary>
    public static JsonObject ToStrict(object schema)
    {
        var node = schema as JsonNode ?? JsonSerializer.SerializeToNode(schema)
            ?? throw new ArgumentException("Schema must serialize to a JSON object.", nameof(schema));
        if (node.DeepClone() is not JsonObject root)
            throw new ArgumentException("Schema must be a JSON object.", nameof(schema));

        Visit(root);
        return root;
    }

    private static void Visit(JsonObject node)
    {
        if (node["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var type))
            node["type"] = type.ToLowerInvariant();

        if (node["properties"] is JsonObject properties)
        {
            foreach (var (_, value) in properties)
            {
                if (value is JsonObject child)
                    Visit(child);
            }

            node["required"] = new JsonArray(properties.Select(pair => (JsonNode?)JsonValue.Create(pair.Key)).ToArray());
        }

        if (node["items"] is JsonObject items)
            Visit(items);

        if (string.Equals(node["type"]?.GetValue<string>(), "object", StringComparison.Ordinal))
            node["additionalProperties"] = false;
    }
}
