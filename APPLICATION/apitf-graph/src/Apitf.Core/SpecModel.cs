using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
using System.Text.Encodings.Web;

namespace Apitf.Core;

/// <summary>Identifies an OpenAPI operation uniquely by route + HTTP verb.</summary>
public readonly record struct OperationKey(string Path, string Method)
{
    public override string ToString() => $"{Method.ToUpperInvariant()} {Path}";
}

/// <summary>A single operation = (path, verb) plus its JSON subtree.</summary>
public sealed class Operation
{
    public required OperationKey Key { get; init; }
    public required JsonNode Node { get; init; }
    public string Canonical => Json.Canonical(Node);
}

/// <summary>
/// Lightweight OpenAPI document wrapper backed by System.Text.Json node tree.
/// Works for JSON and (via conversion) YAML. Deterministic, no LLM.
/// </summary>
public sealed class SpecModel
{
    public static readonly string[] HttpMethods =
        { "get", "put", "post", "delete", "patch", "options", "head", "trace" };

    public JsonObject Root { get; }

    public SpecModel(JsonObject root) => Root = root;

    public JsonObject Paths =>
        Root["paths"] as JsonObject ?? throw new InvalidDataException("spec has no 'paths' object");

    public string Title =>
        (Root["info"] as JsonObject)?["title"]?.GetValue<string>() ?? "API";

    /// <summary>Enumerate all operations deterministically (path order, then fixed verb order).</summary>
    public IEnumerable<Operation> Operations()
    {
        if (Root["paths"] is not JsonObject paths) yield break;
        foreach (var (route, pathItem) in paths)
        {
            if (pathItem is not JsonObject item) continue;
            foreach (var verb in HttpMethods)
            {
                if (item[verb] is JsonNode op)
                    yield return new Operation { Key = new OperationKey(route, verb), Node = op };
            }
        }
    }

    public Dictionary<OperationKey, Operation> OperationMap() =>
        Operations().ToDictionary(o => o.Key, o => o);

    // ---------- IO ----------

    public static SpecModel Load(string filePath)
    {
        var text = File.ReadAllText(filePath);
        return IsYaml(filePath) ? FromYaml(text) : FromJson(text);
    }

    public static SpecModel FromJson(string text)
    {
        var node = JsonNode.Parse(text) ?? throw new InvalidDataException("empty JSON");
        return new SpecModel(node.AsObject());
    }

    public static SpecModel FromYaml(string text)
    {
        // YAML -> object graph -> JSON -> JsonNode (deterministic, lossless for spec content)
        var yaml = new DeserializerBuilder().Build();
        var graph = yaml.Deserialize<object?>(text);
        var json = new SerializerBuilder().JsonCompatible().Build().Serialize(graph!);
        return FromJson(json);
    }

    public void Save(string filePath)
    {
        if (IsYaml(filePath))
            File.WriteAllText(filePath, ToYaml());
        else
            File.WriteAllText(filePath, ToJson());
    }

    public string ToJson()
    {
        var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return Canonicalized().Root.ToJsonString(opts) + "\n";
    }

    public string ToYaml()
    {
        var plain = Json.ToPlain(Canonicalized().Root);
        var ser = new SerializerBuilder().Build();
        return ser.Serialize(plain!);
    }

    /// <summary>Return a copy with verbs ordered canonically within each path (stable diffs).</summary>
    public SpecModel Canonicalized()
    {
        var clone = Root.DeepClone().AsObject();
        if (clone["paths"] is JsonObject paths)
        {
            foreach (var (route, pathItem) in paths.ToList())
            {
                if (pathItem is not JsonObject item) continue;
                var ordered = new JsonObject();
                foreach (var verb in HttpMethods)
                    if (item[verb] is JsonNode n) ordered[verb] = n.DeepClone();
                foreach (var (k, v) in item.ToList())
                    if (!HttpMethods.Contains(k)) ordered[k] = v?.DeepClone();
                paths[route] = ordered;
            }
        }
        return new SpecModel(clone);
    }

    public SpecModel Clone() => new(Root.DeepClone().AsObject());

    public static bool IsYaml(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".yaml" or ".yml";
    }
}

/// <summary>JSON helpers: canonical serialization (for equality/hash) and node->plain conversion.</summary>
public static class Json
{
    /// <summary>Deterministic canonical string: object keys sorted, used for content equality.</summary>
    public static string Canonical(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(node, sb);
        return sb.ToString();
    }

    private static void Write(JsonNode? node, StringBuilder sb)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject o:
                sb.Append('{');
                var first = true;
                foreach (var kv in o.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(kv.Key));
                    sb.Append(':');
                    Write(kv.Value, sb);
                }
                sb.Append('}');
                break;
            case JsonArray a:
                sb.Append('[');
                var f2 = true;
                foreach (var item in a)
                {
                    if (!f2) sb.Append(',');
                    f2 = false;
                    Write(item, sb);
                }
                sb.Append(']');
                break;
            default: // JsonValue scalar
                sb.Append(node.ToJsonString());
                break;
        }
    }

    /// <summary>Convert a JsonNode tree to plain CLR objects for YAML serialization.</summary>
    public static object? ToPlain(JsonNode? node)
    {
        switch (node)
        {
            case null: return null;
            case JsonObject o:
                var dict = new Dictionary<string, object?>();
                foreach (var (k, v) in o) dict[k] = ToPlain(v);
                return dict;
            case JsonArray a:
                return a.Select(ToPlain).ToList();
            case JsonValue val:
                if (val.TryGetValue<bool>(out var b)) return b;
                if (val.TryGetValue<long>(out var l)) return l;
                if (val.TryGetValue<double>(out var d)) return d;
                return val.GetValue<string>();
            default: return node.ToJsonString();
        }
    }
}
