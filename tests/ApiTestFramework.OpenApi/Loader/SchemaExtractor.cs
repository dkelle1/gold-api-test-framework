using ApiTestFramework.OpenApi.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace ApiTestFramework.OpenApi.Loader;

/// <summary>
/// Extracts <see cref="SchemaDefinition"/>s from an OpenAPI document.
/// <para>
/// <see cref="GetRequestSchemas"/> returns only the schemas reachable from
/// request bodies (transitively, through nested objects, arrays and oneOf
/// nullable wrappers) — exactly the set the test-data builders are generated for.
/// </para>
/// </summary>
public static class SchemaExtractor
{
    /// <summary>
    /// Returns the transitive closure of schemas referenced by any request body
    /// in the document. Enums are included so generators can reference them.
    /// </summary>
    public static IReadOnlyList<SchemaDefinition> GetRequestSchemas(OpenApiDocument document)
    {
        var components = document.Components?.Schemas
            ?? new Dictionary<string, OpenApiSchema>();

        // Roots: request body schema refs from all operations
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var operation in pathItem.Operations.Values)
            {
                var refId = operation.RequestBody?.Content?
                    .FirstOrDefault().Value?.Schema?.Reference?.Id;
                if (refId is not null && visited.Add(refId))
                    queue.Enqueue(refId);
            }
        }

        var result = new List<SchemaDefinition>();

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!components.TryGetValue(name, out var schema))
                continue;

            var definition = ToDefinition(name, schema, components);
            result.Add(definition);

            // Follow nested refs
            foreach (var refId in definition.Properties
                         .Select(p => p.RefSchema)
                         .Where(r => r is not null)
                         .Cast<string>())
            {
                if (visited.Add(refId))
                    queue.Enqueue(refId);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns every schema in components/schemas (requests and responses) —
    /// used by response contract validation, where response schemas matter too.
    /// </summary>
    public static IReadOnlyList<SchemaDefinition> GetAllSchemas(OpenApiDocument document)
    {
        var components = document.Components?.Schemas
            ?? new Dictionary<string, OpenApiSchema>();

        return components
            .Select(kv => ToDefinition(kv.Key, kv.Value, components))
            .ToList();
    }

    private static SchemaDefinition ToDefinition(
        string name,
        OpenApiSchema schema,
        IDictionary<string, OpenApiSchema> components)
    {
        if (schema.Enum is { Count: > 0 })
        {
            var values = schema.Enum
                .Select(v => v switch
                {
                    OpenApiString s => s.Value,
                    OpenApiInteger i => i.Value.ToString(),
                    _ => v.ToString() ?? string.Empty
                })
                .ToList();
            return new SchemaDefinition(name, IsEnum: true, values, Array.Empty<SchemaPropertyDefinition>());
        }

        var required = schema.Required ?? new HashSet<string>();
        var properties = new List<SchemaPropertyDefinition>();

        foreach (var (jsonName, propSchema) in schema.Properties ?? new Dictionary<string, OpenApiSchema>())
        {
            properties.Add(ToProperty(jsonName, propSchema, required.Contains(jsonName), components));
        }

        return new SchemaDefinition(name, IsEnum: false, Array.Empty<string>(), properties);
    }

    private static SchemaPropertyDefinition ToProperty(
        string jsonName,
        OpenApiSchema schema,
        bool required,
        IDictionary<string, OpenApiSchema> components)
    {
        var name = ToPascalCase(jsonName);
        var (resolved, refId, nullable) = Resolve(schema, components);

        // Array — describe the element type
        if (resolved.Type == "array" && resolved.Items is not null)
        {
            var (element, elementRefId, _) = Resolve(resolved.Items, components);
            var elementKind = KindOf(element, elementRefId);
            var elementType = elementRefId ?? MapPrimitive(element);
            return new SchemaPropertyDefinition(
                name, jsonName, PropertyKind.Array, elementType, elementRefId,
                elementKind, nullable, required, resolved.Format);
        }

        var kind = KindOf(resolved, refId);
        var clrType = refId ?? MapPrimitive(resolved);
        return new SchemaPropertyDefinition(
            name, jsonName, kind, clrType, refId,
            PropertyKind.Primitive, nullable, required, resolved.Format);
    }

    /// <summary>
    /// Resolves $ref and the "nullable oneOf/allOf wrapper" pattern that
    /// Swashbuckle emits for nullable complex properties.
    /// </summary>
    private static (OpenApiSchema Schema, string? RefId, bool Nullable) Resolve(
        OpenApiSchema schema,
        IDictionary<string, OpenApiSchema> components)
    {
        var nullable = schema.Nullable;

        if (schema.Reference?.Id is { } directRef && components.TryGetValue(directRef, out var direct))
            return (direct, directRef, nullable);

        var wrapped = schema.OneOf?.FirstOrDefault(s => s.Reference is not null)
                   ?? schema.AllOf?.FirstOrDefault(s => s.Reference is not null);
        if (wrapped?.Reference?.Id is { } wrappedRef && components.TryGetValue(wrappedRef, out var target))
            return (target, wrappedRef, nullable);

        return (schema, null, nullable);
    }

    private static PropertyKind KindOf(OpenApiSchema schema, string? refId)
    {
        if (refId is null) return PropertyKind.Primitive;
        return schema.Enum is { Count: > 0 } ? PropertyKind.Enum : PropertyKind.Object;
    }

    private static string MapPrimitive(OpenApiSchema schema) => schema.Type switch
    {
        "string" when schema.Format == "date-time" => "DateTimeOffset",
        "string" => "string",
        "integer" when schema.Format == "int64" => "long",
        "integer" => "int",
        "number" => "decimal",
        "boolean" => "bool",
        _ => "object"
    };

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
