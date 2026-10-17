using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace ApiTestFramework.Contracts;

/// <summary>
/// Validates a JSON value against an OpenAPI 3.0 schema and returns every violation found
/// (empty = conforms). Typed deserialization silently tolerates drift — extra fields,
/// missing fields, an enum sent as a number instead of a string — this validator does not.
/// <para>
/// Supported: type, nullable, properties, required, additionalProperties, items, enum
/// (kind-sensitive), allOf / oneOf / anyOf / not, discriminator, string formats
/// (date-time, date, uuid, email, uri), minLength / maxLength / pattern,
/// minimum / maximum (incl. exclusive), minItems / maxItems.
/// </para>
/// </summary>
public sealed class JsonSchemaValidator
{
    private const int MaxDepth = 64;
    private readonly bool _strictObjects;
    private readonly Func<string, OpenApiSchema?> _resolveSchema;

    /// <param name="strictObjects">
    /// When true (default), an object may not contain properties its schema does not declare
    /// (unless <c>additionalProperties</c> is a schema) and a declared, non-nullable property
    /// must be present. Catches response drift that the OpenAPI default (additional properties
    /// allowed, optional fields) would let through.
    /// </param>
    /// <param name="resolveSchema">Resolves a schema by component name (for discriminator mappings).</param>
    public JsonSchemaValidator(bool strictObjects = true, Func<string, OpenApiSchema?>? resolveSchema = null)
    {
        _strictObjects = strictObjects;
        _resolveSchema = resolveSchema ?? (_ => null);
    }

    public IReadOnlyList<string> Validate(JsonElement value, OpenApiSchema schema, string path = "$")
    {
        var errors = new List<string>();
        Validate(value, schema, path, 0, errors);
        return errors;
    }

    private void Validate(JsonElement value, OpenApiSchema schema, string path, int depth, List<string> errors)
    {
        if (depth > MaxDepth)
            return;

        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!schema.Nullable && !IsAnySchema(schema) && !AllowsNullThroughComposition(schema, depth))
                errors.Add($"{path}: null is not allowed");
            return;
        }

        if (schema.Enum.Count > 0 && !schema.Enum.Any(member => EnumMatches(member, value)))
            errors.Add($"{path}: value {Render(value)} not in enum [{string.Join(", ", schema.Enum.Select(RenderEnum))}]");

        if (schema.Not is not null && Matches(value, schema.Not, path, depth))
            errors.Add($"{path}: must not match the 'not' schema");

        if (schema.AnyOf.Count > 0 && !schema.AnyOf.Any(s => Matches(value, s, path, depth)))
            errors.Add($"{path}: matches none of the anyOf schemas");

        if (schema.OneOf.Count > 0)
            ValidateOneOf(value, schema, path, depth, errors);

        var type = EffectiveType(schema);
        switch (type)
        {
            case "object":
                ValidateObject(value, schema, path, depth, errors);
                break;
            case "array":
                ValidateArray(value, schema, path, depth, errors);
                break;
            case "string":
                ValidateString(value, schema, path, errors);
                break;
            case "integer":
                ValidateInteger(value, schema, path, errors);
                break;
            case "number":
                ValidateNumber(value, schema, path, errors);
                break;
            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    errors.Add($"{path}: expected boolean, got {Kind(value)}");
                break;
        }

        // allOf parts that are not plain object fragments (objects are merged in ValidateObject)
        if (type != "object")
        {
            foreach (var part in schema.AllOf)
                Validate(value, part, path, depth + 1, errors);
        }
    }

    // ── objects ──────────────────────────────────────────────────────────────

    private void ValidateObject(JsonElement value, OpenApiSchema schema, string path, int depth, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: expected object, got {Kind(value)}");
            return;
        }

        var properties = new Dictionary<string, OpenApiSchema>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var additionalSchema = schema.AdditionalProperties;
        var additionalAllowed = schema.AdditionalPropertiesAllowed;
        CollectObjectShape(schema, properties, required, ref additionalSchema, ref additionalAllowed, depth);

        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            present.Add(property.Name);
            var propertyPath = $"{path}.{property.Name}";
            if (properties.TryGetValue(property.Name, out var propertySchema))
                Validate(property.Value, propertySchema, propertyPath, depth + 1, errors);
            else if (additionalSchema is not null)
                Validate(property.Value, additionalSchema, propertyPath, depth + 1, errors);
            else if (!additionalAllowed || _strictObjects)
                errors.Add($"{propertyPath}: property is not declared in the schema");
        }

        foreach (var name in required.Where(name => !present.Contains(name)))
            errors.Add($"{path}.{name}: required property is missing");

        if (_strictObjects)
        {
            foreach (var (name, propertySchema) in properties)
            {
                if (!present.Contains(name) && !required.Contains(name) && !IsNullable(propertySchema, depth))
                    errors.Add($"{path}.{name}: non-nullable property is missing");
            }
        }
    }

    private static void CollectObjectShape(
        OpenApiSchema schema,
        Dictionary<string, OpenApiSchema> properties,
        HashSet<string> required,
        ref OpenApiSchema? additionalSchema,
        ref bool additionalAllowed,
        int depth)
    {
        if (depth > MaxDepth)
            return;

        foreach (var (name, property) in schema.Properties)
            properties.TryAdd(name, property);
        required.UnionWith(schema.Required);
        additionalSchema ??= schema.AdditionalProperties;
        additionalAllowed &= schema.AdditionalPropertiesAllowed;

        foreach (var part in schema.AllOf)
            CollectObjectShape(part, properties, required, ref additionalSchema, ref additionalAllowed, depth + 1);
    }

    // ── composition ──────────────────────────────────────────────────────────

    private void ValidateOneOf(JsonElement value, OpenApiSchema schema, string path, int depth, List<string> errors)
    {
        var discriminated = ResolveDiscriminator(value, schema);
        if (discriminated is not null)
        {
            Validate(value, discriminated, path, depth + 1, errors);
            return;
        }

        var matches = schema.OneOf.Count(s => Matches(value, s, path, depth));
        if (matches == 0)
        {
            // Report the closest candidate's errors — far more useful than "matches none"
            var best = schema.OneOf
                .Select(s => { var e = new List<string>(); Validate(value, s, path, depth + 1, e); return e; })
                .OrderBy(e => e.Count)
                .First();
            errors.Add($"{path}: matches none of the oneOf schemas");
            errors.AddRange(best);
        }
        else if (matches > 1)
        {
            errors.Add($"{path}: matches {matches} oneOf schemas (exactly one expected)");
        }
    }

    private OpenApiSchema? ResolveDiscriminator(JsonElement value, OpenApiSchema schema)
    {
        var propertyName = schema.Discriminator?.PropertyName;
        if (propertyName is null || value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty(propertyName, out var tag) || tag.ValueKind != JsonValueKind.String)
            return null;

        var tagValue = tag.GetString()!;
        if (schema.Discriminator!.Mapping.TryGetValue(tagValue, out var reference))
            return _resolveSchema(reference.Split('/').Last());

        return schema.OneOf.FirstOrDefault(s => s.Reference?.Id == tagValue);
    }

    private bool Matches(JsonElement value, OpenApiSchema schema, string path, int depth)
    {
        var errors = new List<string>();
        Validate(value, schema, path, depth + 1, errors);
        return errors.Count == 0;
    }

    private bool AllowsNullThroughComposition(OpenApiSchema schema, int depth)
        => depth <= MaxDepth
           && (schema.OneOf.Any(s => s.Nullable) || schema.AnyOf.Any(s => s.Nullable));

    private bool IsNullable(OpenApiSchema schema, int depth)
        => schema.Nullable || IsAnySchema(schema) || AllowsNullThroughComposition(schema, depth);

    // ── arrays and scalars ───────────────────────────────────────────────────

    private void ValidateArray(JsonElement value, OpenApiSchema schema, string path, int depth, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{path}: expected array, got {Kind(value)}");
            return;
        }

        var length = value.GetArrayLength();
        if (schema.MinItems is { } min && length < min)
            errors.Add($"{path}: expected at least {min} items, got {length}");
        if (schema.MaxItems is { } max && length > max)
            errors.Add($"{path}: expected at most {max} items, got {length}");

        if (schema.Items is null)
            return;

        var index = 0;
        foreach (var item in value.EnumerateArray())
            Validate(item, schema.Items, $"{path}[{index++}]", depth + 1, errors);
    }

    private static void ValidateString(JsonElement value, OpenApiSchema schema, string path, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}: expected string, got {Kind(value)} ({Render(value)})");
            return;
        }

        var text = value.GetString()!;
        if (schema.MinLength is { } minLength && text.Length < minLength)
            errors.Add($"{path}: shorter than minLength {minLength}");
        if (schema.MaxLength is { } maxLength && text.Length > maxLength)
            errors.Add($"{path}: longer than maxLength {maxLength}");
        if (!string.IsNullOrEmpty(schema.Pattern) && !Regex.IsMatch(text, schema.Pattern))
            errors.Add($"{path}: does not match pattern {schema.Pattern}");

        var formatOk = schema.Format switch
        {
            "date-time" => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            "date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "uuid" => Guid.TryParse(text, out _),
            "email" => text.IndexOf('@') > 0 && text.IndexOf('@') < text.Length - 1,
            "uri" => Uri.TryCreate(text, UriKind.Absolute, out _),
            _ => true
        };
        if (!formatOk)
            errors.Add($"{path}: \"{Truncate(text)}\" is not a valid {schema.Format}");
    }

    private static void ValidateInteger(JsonElement value, OpenApiSchema schema, string path, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            errors.Add($"{path}: expected integer, got {Kind(value)} ({Render(value)})");
            return;
        }

        if (schema.Format == "int32" && number is < int.MinValue or > int.MaxValue)
            errors.Add($"{path}: {number} is out of int32 range");
        CheckRange(number, schema, path, errors);
    }

    private static void ValidateNumber(JsonElement value, OpenApiSchema schema, string path, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"{path}: expected number, got {Kind(value)} ({Render(value)})");
            return;
        }

        CheckRange(value.GetDecimal(), schema, path, errors);
    }

    private static void CheckRange(decimal number, OpenApiSchema schema, string path, List<string> errors)
    {
        if (schema.Minimum is { } min && (schema.ExclusiveMinimum == true ? number <= min : number < min))
            errors.Add(FormattableString.Invariant($"{path}: {number} is below the minimum {min}"));
        if (schema.Maximum is { } max && (schema.ExclusiveMaximum == true ? number >= max : number > max))
            errors.Add(FormattableString.Invariant($"{path}: {number} is above the maximum {max}"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string? EffectiveType(OpenApiSchema schema)
    {
        if (!string.IsNullOrEmpty(schema.Type))
            return schema.Type;
        if (schema.Properties.Count > 0 || schema.AllOf.Any(p => EffectiveType(p) == "object"))
            return "object";
        if (schema.Items is not null)
            return "array";
        return null;
    }

    private static bool IsAnySchema(OpenApiSchema schema)
        => EffectiveType(schema) is null && schema.OneOf.Count == 0 && schema.AnyOf.Count == 0
           && schema.AllOf.Count == 0 && schema.Enum.Count == 0;

    private static bool EnumMatches(IOpenApiAny member, JsonElement value) => member switch
    {
        OpenApiString s => value.ValueKind == JsonValueKind.String && value.GetString() == s.Value,
        OpenApiInteger i => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n == i.Value,
        OpenApiLong l => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n == l.Value,
        OpenApiDouble d => value.ValueKind == JsonValueKind.Number && value.GetDouble().Equals(d.Value),
        OpenApiFloat f => value.ValueKind == JsonValueKind.Number && value.GetDouble().Equals(f.Value),
        OpenApiBoolean b => value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean() == b.Value,
        OpenApiNull => value.ValueKind == JsonValueKind.Null,
        _ => false
    };

    private static string RenderEnum(IOpenApiAny member) => member switch
    {
        OpenApiString s => $"'{s.Value}'",
        OpenApiInteger i => i.Value.ToString(CultureInfo.InvariantCulture),
        OpenApiLong l => l.Value.ToString(CultureInfo.InvariantCulture),
        OpenApiDouble d => d.Value.ToString(CultureInfo.InvariantCulture),
        OpenApiFloat f => f.Value.ToString(CultureInfo.InvariantCulture),
        OpenApiBoolean b => b.Value ? "true" : "false",
        _ => "null"
    };

    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => value.ValueKind.ToString().ToLowerInvariant()
    };

    private static string Render(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? $"'{Truncate(value.GetString()!)}'" : Truncate(value.GetRawText());

    private static string Truncate(string text) => text.Length <= 60 ? text : text[..57] + "...";
}
