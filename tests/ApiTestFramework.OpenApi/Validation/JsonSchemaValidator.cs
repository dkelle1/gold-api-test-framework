using System.Text.Json;
using System.Text.Json.Nodes;
using ApiTestFramework.OpenApi.Models;

namespace ApiTestFramework.OpenApi.Validation;

/// <summary>
/// Validates a JSON payload (typically a live API response) against a named
/// schema from the swagger spec. Catches contract drift that typed
/// deserialization silently swallows: unknown properties the DTO does not
/// declare, missing non-nullable properties, and type mismatches — at every
/// nesting level, including arrays.
/// </summary>
public static class JsonSchemaValidator
{
    /// <summary>
    /// Returns a list of violations; empty means the payload conforms.
    /// </summary>
    /// <param name="json">The JSON document to validate.</param>
    /// <param name="rootSchemaName">Schema name from components/schemas, e.g. "Order".</param>
    /// <param name="schemas">All schemas (SchemaExtractor.GetAllSchemas).</param>
    public static IReadOnlyList<string> Validate(
        string json,
        string rootSchemaName,
        IReadOnlyList<SchemaDefinition> schemas)
    {
        var byName = schemas.ToDictionary(s => s.Name);
        if (!byName.TryGetValue(rootSchemaName, out var root))
            throw new ArgumentException($"Unknown schema: {rootSchemaName}");

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return new[] { $"$: not valid JSON — {ex.Message}" };
        }

        var violations = new List<string>();
        ValidateAgainstSchema(node, root, "$", byName, violations);
        return violations;
    }

    private static void ValidateAgainstSchema(
        JsonNode? node,
        SchemaDefinition schema,
        string path,
        IReadOnlyDictionary<string, SchemaDefinition> schemas,
        List<string> violations)
    {
        if (schema.IsEnum)
        {
            ValidateEnum(node, path, violations);
            return;
        }

        if (node is not JsonObject obj)
        {
            violations.Add($"{path}: expected object ({schema.Name}), got {Describe(node)}");
            return;
        }

        var declared = schema.Properties.ToDictionary(p => p.JsonName, StringComparer.OrdinalIgnoreCase);

        // Unknown properties — the service returns fields the contract does not declare
        foreach (var key in obj.Select(kv => kv.Key))
        {
            if (!declared.ContainsKey(key))
                violations.Add($"{path}.{key}: property not declared in schema {schema.Name}");
        }

        foreach (var prop in schema.Properties)
        {
            var propPath = $"{path}.{prop.JsonName}";
            var present = obj.TryGetPropertyValue(prop.JsonName, out var value);

            if (!present || value is null)
            {
                if (!prop.Nullable && present)
                    violations.Add($"{propPath}: null but schema marks it non-nullable");
                else if (!prop.Nullable && !present)
                    violations.Add($"{propPath}: missing but schema marks it non-nullable");
                continue;
            }

            ValidateProperty(value, prop, propPath, schemas, violations);
        }
    }

    private static void ValidateProperty(
        JsonNode value,
        SchemaPropertyDefinition prop,
        string path,
        IReadOnlyDictionary<string, SchemaDefinition> schemas,
        List<string> violations)
    {
        switch (prop.Kind)
        {
            case PropertyKind.Object:
                ValidateAgainstSchema(value, schemas[prop.RefSchema!], path, schemas, violations);
                break;

            case PropertyKind.Enum:
                ValidateEnum(value, path, violations);
                break;

            case PropertyKind.Array:
                if (value is not JsonArray array)
                {
                    violations.Add($"{path}: expected array, got {Describe(value)}");
                    break;
                }
                for (var i = 0; i < array.Count; i++)
                {
                    var elementPath = $"{path}[{i}]";
                    var element = array[i];
                    if (prop.ElementKind == PropertyKind.Object)
                        ValidateAgainstSchema(element, schemas[prop.RefSchema!], elementPath, schemas, violations);
                    else if (prop.ElementKind == PropertyKind.Enum)
                        ValidateEnum(element, elementPath, violations);
                    else
                        ValidatePrimitive(element, prop.ClrType, elementPath, violations);
                }
                break;

            default:
                ValidatePrimitive(value, prop.ClrType, path, violations);
                break;
        }
    }

    private static void ValidatePrimitive(
        JsonNode? value, string clrType, string path, List<string> violations)
    {
        var kind = value?.GetValueKind() ?? JsonValueKind.Null;
        var ok = clrType switch
        {
            "string" or "DateTimeOffset" => kind == JsonValueKind.String,
            "int" or "long" or "decimal" => kind == JsonValueKind.Number,
            "bool" => kind is JsonValueKind.True or JsonValueKind.False,
            _ => true // free-form object — nothing to check
        };

        if (!ok)
            violations.Add($"{path}: expected {clrType}, got {Describe(value)}");
    }

    private static void ValidateEnum(JsonNode? value, string path, List<string> violations)
    {
        // Enums are serialized as integers by these services, but string names
        // are also a valid wire format (JsonStringEnumConverter) — accept both.
        var kind = value?.GetValueKind() ?? JsonValueKind.Null;
        if (kind is not (JsonValueKind.Number or JsonValueKind.String))
            violations.Add($"{path}: expected enum (number or string), got {Describe(value)}");
    }

    private static string Describe(JsonNode? node) =>
        node?.GetValueKind().ToString().ToLowerInvariant() ?? "null";
}
