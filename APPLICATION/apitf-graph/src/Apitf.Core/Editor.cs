using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Apitf.Core;

/// <summary>Deterministic creation / editing of operations. Mirrors apitf.py semantics.</summary>
public static class SpecEditor
{
    /// <summary>Create an empty but valid OpenAPI 3.0 document from scratch.</summary>
    public static SpecModel NewSpec(string title)
    {
        var root = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject { ["title"] = title, ["version"] = "1.0.0" },
            ["paths"] = new JsonObject()
        };
        return new SpecModel(root);
    }

    /// <summary>Add an operation idempotently. Path parameters are derived from {tokens}.</summary>
    public static bool AddOperation(SpecModel spec, string method, string path,
                                    string? summary = null, string? operationId = null,
                                    bool force = false)
    {
        var verb = method.ToLowerInvariant();
        if (!SpecModel.HttpMethods.Contains(verb))
            throw new ArgumentException($"invalid HTTP method '{method}'");

        var paths = spec.Root["paths"] as JsonObject;
        if (paths is null) { paths = new JsonObject(); spec.Root["paths"] = paths; }

        var item = paths[path] as JsonObject;
        if (item is null) { item = new JsonObject(); paths[path] = item; }

        if (item.ContainsKey(verb) && !force) return false; // already present, no change

        var op = new JsonObject
        {
            ["operationId"] = operationId ??
                verb + Regex.Replace(path, "[/{}]", "_").Trim('_'),
            ["summary"] = summary ?? $"{verb.ToUpperInvariant()} {path}",
            ["responses"] = new JsonObject
            {
                ["200"] = new JsonObject { ["description"] = "OK" }
            }
        };

        var tokens = Regex.Matches(path, "{([^}]+)}").Select(m => m.Groups[1].Value).ToList();
        if (tokens.Count > 0)
        {
            var parameters = new JsonArray();
            foreach (var tok in tokens)
                parameters.Add(new JsonObject
                {
                    ["name"] = tok,
                    ["in"] = "path",
                    ["required"] = true,
                    ["schema"] = new JsonObject { ["type"] = "string" }
                });
            op["parameters"] = parameters;
        }

        item[verb] = op;
        return true;
    }

    /// <summary>Remove an operation idempotently. Empty path items are pruned.</summary>
    public static bool RemoveOperation(SpecModel spec, string method, string path)
    {
        var verb = method.ToLowerInvariant();
        if (spec.Root["paths"] is not JsonObject paths) return false;
        if (paths[path] is not JsonObject item || !item.ContainsKey(verb)) return false;

        item.Remove(verb);
        if (!SpecModel.HttpMethods.Any(item.ContainsKey))
            paths.Remove(path);
        return true;
    }
}
