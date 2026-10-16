namespace ApiTestFramework.OpenApi.Models;

/// <summary>
/// Kind of a schema property, used by the builder scaffolder to decide
/// how to generate defaults and With/Add methods.
/// </summary>
public enum PropertyKind
{
    Primitive,
    Enum,
    Object,
    Array
}

/// <summary>
/// A single property of an object schema, resolved against components/schemas.
/// For <see cref="PropertyKind.Array"/> properties, <see cref="ClrType"/> and
/// <see cref="RefSchema"/> describe the element type.
/// </summary>
public record SchemaPropertyDefinition(
    string Name,
    string JsonName,
    PropertyKind Kind,
    string ClrType,
    string? RefSchema,
    PropertyKind ElementKind,
    bool Nullable,
    bool Required,
    string? Format);

/// <summary>
/// A named schema from an OpenAPI document (object or enum),
/// flattened into a form the code generators can consume.
/// </summary>
public record SchemaDefinition(
    string Name,
    bool IsEnum,
    IReadOnlyList<string> EnumValues,
    IReadOnlyList<SchemaPropertyDefinition> Properties);
