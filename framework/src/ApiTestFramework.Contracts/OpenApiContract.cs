using System.Text.RegularExpressions;
using ApiTestFramework.OpenApi.Loader;
using Microsoft.OpenApi.Models;

namespace ApiTestFramework.Contracts;

/// <summary>One OpenAPI document and its operations, ready for request → operation lookup.</summary>
public sealed class OpenApiContract
{
    private OpenApiContract(string name, string sourcePath, OpenApiDocument document)
    {
        Name = name;
        SourcePath = sourcePath;
        Document = document;
        Operations = document.Paths
            .SelectMany(path => path.Value.Operations.Select(op => new ContractOperation(
                op.Key.ToString().ToUpperInvariant(), path.Key, op.Value)))
            .ToList();
    }

    /// <summary>Contract name — the ApiClient / service name it applies to.</summary>
    public string Name { get; }

    public string SourcePath { get; }

    public OpenApiDocument Document { get; }

    public IReadOnlyList<ContractOperation> Operations { get; }

    public static OpenApiContract Load(string name, string path)
        => new(name, path, OpenApiSpecLoader.LoadFromFile(path));

    public OpenApiSchema? ResolveSchema(string componentName)
        => Document.Components?.Schemas.TryGetValue(componentName, out var schema) == true ? schema : null;

    /// <summary>
    /// Finds the operation for a request. With a route template (requests built by RequestBuilder)
    /// the match is exact; otherwise the concrete path is matched against every template and the
    /// most specific one (most literal segments) wins, so "/api/products/category/x" resolves to
    /// "/api/products/category/{category}", not "/api/products/{id}".
    /// </summary>
    public ContractOperation? Find(string method, string path, string? template = null)
    {
        method = method.ToUpperInvariant();
        if (!string.IsNullOrEmpty(template))
        {
            var normalizedTemplate = Normalize(template);
            var exact = Operations.FirstOrDefault(op => op.Method == method
                && string.Equals(op.PathTemplate, normalizedTemplate, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;
        }

        var normalizedPath = Normalize(path);
        return Operations
            .Where(op => op.Method == method && op.Pattern.IsMatch(normalizedPath))
            .OrderByDescending(op => op.LiteralSegments)
            .FirstOrDefault();
    }

    internal static string Normalize(string path)
    {
        var withoutQuery = path.Split('?', 2)[0];
        var withSlash = withoutQuery.StartsWith('/') ? withoutQuery : "/" + withoutQuery;
        return withSlash.Length > 1 ? withSlash.TrimEnd('/') : withSlash;
    }
}

/// <summary>An operation of a contract (method + route template) with its documented responses.</summary>
public sealed class ContractOperation
{
    public ContractOperation(string method, string pathTemplate, OpenApiOperation operation)
    {
        Method = method;
        PathTemplate = OpenApiContract.Normalize(pathTemplate);
        Operation = operation;

        var segments = PathTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        LiteralSegments = segments.Count(s => !s.StartsWith('{'));
        var pattern = string.Join("/", segments.Select(s => s.StartsWith('{') ? "[^/]+" : Regex.Escape(s)));
        Pattern = new Regex($"^/{pattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public string Method { get; }

    public string PathTemplate { get; }

    public OpenApiOperation Operation { get; }

    internal Regex Pattern { get; }

    internal int LiteralSegments { get; }

    public override string ToString() => $"{Method} {PathTemplate}";

    /// <summary>
    /// The documented response for a status code: exact code, then the "4XX" range, then "default".
    /// </summary>
    public (string Key, OpenApiResponse Response)? FindResponse(int statusCode)
    {
        foreach (var key in new[] { statusCode.ToString(), $"{statusCode / 100}XX", "default" })
        {
            var match = Operation.Responses.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (match.Value is not null)
                return (match.Key, match.Value);
        }
        return null;
    }
}
