using ApiTestFramework.OpenApi.Models;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace ApiTestFramework.OpenApi.Loader;

/// <summary>
/// Loads and parses OpenAPI specification files.
/// Use <see cref="LoadFromFile"/> to get an <see cref="OpenApiDocument"/>,
/// then <see cref="GetEndpoints"/> to extract endpoint definitions.
/// </summary>
public static class OpenApiSpecLoader
{
    /// <summary>
    /// Loads an OpenAPI document from a swagger.json file.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the swagger.json file.</param>
    public static OpenApiDocument LoadFromFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var reader = new OpenApiStreamReader();
        var document = reader.Read(stream, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var errors = string.Join(", ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Failed to parse OpenAPI spec '{filePath}': {errors}");
        }

        return document;
    }

    /// <summary>
    /// Extracts a flat list of <see cref="EndpointDefinition"/> from an OpenAPI document.
    /// </summary>
    public static IReadOnlyList<EndpointDefinition> GetEndpoints(OpenApiDocument document)
    {
        var result = new List<EndpointDefinition>();

        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (operationType, operation) in pathItem.Operations)
            {
                var parameters = (operation.Parameters ?? [])
                    .Select(p => new ParameterDefinition(
                        p.Name,
                        p.In?.ToString()?.ToLowerInvariant() ?? "query",
                        p.Schema?.Type ?? "string",
                        p.Required))
                    .ToList();

                var responses = operation.Responses?
                    .ToDictionary(
                        r => int.TryParse(r.Key, out var code) ? code : 0,
                        r => r.Value.Content?.FirstOrDefault().Value?.Schema?.Reference?.Id)
                    ?? new Dictionary<int, string?>();

                var requiresAuth = operation.Security?.Count > 0
                    || document.SecurityRequirements?.Count > 0;

                var requestBodySchema = operation.RequestBody?.Content
                    ?.FirstOrDefault().Value?.Schema?.Reference?.Id;

                result.Add(new EndpointDefinition(
                    path,
                    operationType.ToString().ToUpperInvariant(),
                    operation.OperationId ?? $"{operationType}_{path.Replace("/", "_").Trim('_')}",
                    operation.Tags?.FirstOrDefault()?.Name,
                    requestBodySchema,
                    parameters,
                    responses,
                    requiresAuth));
            }
        }

        return result;
    }
}
