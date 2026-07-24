using System.Text.Json.Nodes;
using ApiTestFramework.OpenApi.Models;

namespace ApiTestFramework.OpenApi.Generator;

/// <summary>
/// A single schema-driven negative test case: a payload that violates exactly
/// one constraint of the request schema and must be rejected with 400.
/// </summary>
public record NegativeCase(string Name, string Description, string JsonPayload);

/// <summary>
/// Derives negative (expected-400) request payloads from schema constraints.
/// <para>
/// A valid baseline payload is built from the schema (nested objects and
/// arrays included), then one mutation is applied per case:
/// each <c>required</c> property — at any nesting depth — is removed, and
/// each required array is emptied. Exactly one violation per payload, so a
/// failure points at a specific validation rule.
/// </para>
/// <para>
/// Only <c>required</c>-based mutations are generated: they are the ones this
/// API contract actually promises to reject. Format violations (e.g. a bad
/// email) are not generated because the services do not validate formats —
/// a generated 400-expectation would be wrong.
/// </para>
/// </summary>
public static class NegativeCaseGenerator
{
    /// <summary>
    /// Generates negative cases for the given root request schema.
    /// </summary>
    /// <param name="rootSchemaName">e.g. "CreateOrderRequest"</param>
    /// <param name="schemas">All request schemas (SchemaExtractor.GetRequestSchemas).</param>
    public static IReadOnlyList<NegativeCase> ForSchema(
        string rootSchemaName,
        IReadOnlyList<SchemaDefinition> schemas)
    {
        var byName = schemas.ToDictionary(s => s.Name);
        if (!byName.TryGetValue(rootSchemaName, out var root) || root.IsEnum)
            throw new ArgumentException($"Unknown or non-object schema: {rootSchemaName}");

        var cases = new List<NegativeCase>();

        // Collect mutation targets: (path, property) for every required
        // property reachable from the root.
        foreach (var (path, prop) in CollectRequiredProperties(root, byName, parentPath: ""))
        {
            var withoutProp = BuildBaseline(root, byName);
            RemoveAtPath(withoutProp, path);
            cases.Add(new NegativeCase(
                Name: $"Missing_{path.Replace('.', '_')}",
                Description: $"Required property '{path}' is missing",
                JsonPayload: withoutProp.ToJsonString()));

            if (prop.Kind == PropertyKind.Array)
            {
                var withEmptyArray = BuildBaseline(root, byName);
                SetEmptyArrayAtPath(withEmptyArray, path);
                cases.Add(new NegativeCase(
                    Name: $"Empty_{path.Replace('.', '_')}",
                    Description: $"Required array '{path}' is empty",
                    JsonPayload: withEmptyArray.ToJsonString()));
            }
        }

        return cases;
    }

    /// <summary>
    /// Builds a structurally valid payload for the schema: required and
    /// non-nullable properties filled with plausible sample values, optional
    /// nullable properties omitted (smallest valid payload).
    /// </summary>
    public static JsonObject BuildBaseline(
        SchemaDefinition schema,
        IReadOnlyDictionary<string, SchemaDefinition> schemas)
    {
        var obj = new JsonObject();

        foreach (var prop in schema.Properties)
        {
            if (prop.Nullable && !prop.Required) continue;

            obj[prop.JsonName] = prop.Kind switch
            {
                PropertyKind.Object => BuildBaseline(schemas[prop.RefSchema!], schemas),
                PropertyKind.Array => new JsonArray(BuildElement(prop, schemas)),
                PropertyKind.Enum => JsonValue.Create(0), // enums serialize as ints in these services
                _ => SamplePrimitive(prop)
            };
        }

        return obj;
    }

    private static JsonNode BuildElement(
        SchemaPropertyDefinition arrayProp,
        IReadOnlyDictionary<string, SchemaDefinition> schemas)
    {
        return arrayProp.ElementKind switch
        {
            PropertyKind.Object => BuildBaseline(schemas[arrayProp.RefSchema!], schemas),
            PropertyKind.Enum => JsonValue.Create(0),
            _ => SamplePrimitive(arrayProp)
        };
    }

    private static JsonNode SamplePrimitive(SchemaPropertyDefinition prop)
    {
        var name = prop.JsonName.ToLowerInvariant();
        return prop.ClrType switch
        {
            "string" when name.Contains("email") || prop.Format == "email"
                => JsonValue.Create("negative.tests@example.com"),
            "string" when prop.Format == "date-time"
                => JsonValue.Create("2024-01-01T00:00:00Z"),
            "string" => JsonValue.Create("negative-test-value"),
            "int" or "long" => JsonValue.Create(1),
            "decimal" => JsonValue.Create(9.99),
            "bool" => JsonValue.Create(true),
            "DateTimeOffset" => JsonValue.Create("2024-01-01T00:00:00Z"),
            _ => JsonValue.Create("negative-test-value")
        };
    }

    private static IEnumerable<(string Path, SchemaPropertyDefinition Prop)> CollectRequiredProperties(
        SchemaDefinition schema,
        IReadOnlyDictionary<string, SchemaDefinition> schemas,
        string parentPath)
    {
        foreach (var prop in schema.Properties)
        {
            var path = parentPath.Length == 0 ? prop.JsonName : $"{parentPath}.{prop.JsonName}";

            if (prop.Required)
                yield return (path, prop);

            // Recurse into nested objects that are part of the baseline
            // (required or non-nullable) — their required props matter too.
            if (prop.Kind == PropertyKind.Object && (!prop.Nullable || prop.Required))
            {
                foreach (var nested in CollectRequiredProperties(schemas[prop.RefSchema!], schemas, path))
                    yield return nested;
            }

            // Required props inside array elements: mutate the first element
            if (prop.Kind == PropertyKind.Array
                && prop.ElementKind == PropertyKind.Object
                && (!prop.Nullable || prop.Required))
            {
                foreach (var nested in CollectRequiredProperties(schemas[prop.RefSchema!], schemas, $"{path}[0]"))
                    yield return nested;
            }
        }
    }

    private static void RemoveAtPath(JsonObject root, string path)
    {
        var (parent, last) = Traverse(root, path);
        parent?.Remove(last);
    }

    private static void SetEmptyArrayAtPath(JsonObject root, string path)
    {
        var (parent, last) = Traverse(root, path);
        if (parent is not null) parent[last] = new JsonArray();
    }

    /// <summary>
    /// Walks a dotted path with optional [index] segments (e.g. "items[0].productId")
    /// and returns the parent object plus the final property name.
    /// </summary>
    private static (JsonObject? Parent, string Last) Traverse(JsonObject root, string path)
    {
        var segments = path.Split('.');
        JsonNode? current = root;

        for (var i = 0; i < segments.Length - 1; i++)
            current = Step(current, segments[i]);

        // The final segment is always a plain property name — indexed segments
        // ("items[0]") only ever appear as intermediate steps.
        return (current as JsonObject, segments[^1]);

        static JsonNode? Step(JsonNode? node, string segment)
        {
            var bracket = segment.IndexOf('[');
            if (bracket < 0) return node?[segment];
            var name = segment[..bracket];
            var index = int.Parse(segment[(bracket + 1)..^1]);
            return node?[name]?[index];
        }
    }
}
