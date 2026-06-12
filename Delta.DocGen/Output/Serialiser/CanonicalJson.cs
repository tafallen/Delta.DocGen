using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Delta.DocGen.Model;

namespace Delta.DocGen.Output.Serialiser;

public static class CanonicalJson
{
    private static readonly JsonSerializerOptions _canonicalOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions _prettyOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static CanonicalJson()
    {
        _canonicalOptions.MakeReadOnly();
        _prettyOptions.MakeReadOnly();
    }

    public static string Serialise(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, _canonicalOptions);
        var sorted = SortKeys(node)!;
        return sorted.ToJsonString();
    }

    public static void Write(Envelope envelope, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (dir is not null && dir.Length > 0)
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(envelope, _prettyOptions);
        File.WriteAllText(outputPath, json, Encoding.UTF8);
    }

    private static JsonNode? SortKeys(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var sorted = new JsonObject();
            foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                sorted[kvp.Key] = SortKeys(kvp.Value);
            return sorted;
        }
        if (node is JsonArray arr)
        {
            var result = new JsonArray();
            foreach (var item in arr)
                result.Add(SortKeys(item));
            return result;
        }
        return node?.DeepClone();
    }
}
