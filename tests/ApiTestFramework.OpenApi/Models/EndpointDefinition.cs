namespace ApiTestFramework.OpenApi.Models;

/// <summary>
/// Represents a parsed API endpoint from an OpenAPI specification.
/// Used as input to TestCaseScaffolder to generate test case definitions.
/// </summary>
public record EndpointDefinition(
    string Path,
    string Method,
    string OperationId,
    string? Tag,
    string? RequestBodySchema,
    IReadOnlyList<ParameterDefinition> Parameters,
    IReadOnlyDictionary<int, string?> Responses,
    bool RequiresAuth);

/// <summary>
/// Represents a single parameter of an endpoint (path, query, or header).
/// </summary>
public record ParameterDefinition(
    string Name,
    string In,
    string Type,
    bool Required);
