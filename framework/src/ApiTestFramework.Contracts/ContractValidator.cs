using System.Text.Json;
using Microsoft.OpenApi.Models;

namespace ApiTestFramework.Contracts;

/// <summary>Outcome of checking one response against the contract.</summary>
public sealed record ContractCheck(
    string? Contract,
    string Method,
    string Path,
    string? OperationTemplate,
    int StatusCode,
    IReadOnlyList<string> Violations)
{
    public bool IsValid => Violations.Count == 0;

    public string Describe()
    {
        var operation = OperationTemplate is null ? $"{Method} {Path}" : $"{Method} {OperationTemplate} ({Path})";
        return IsValid
            ? $"{operation} → {StatusCode} conforms to the {Contract} contract"
            : $"Contract violation{(Violations.Count > 1 ? "s" : "")} for {operation} → {StatusCode}"
              + (Contract is null ? "" : $" [{Contract}]") + ":"
              + string.Concat(Violations.Select(v => $"{Environment.NewLine}  - {v}"));
    }
}

/// <summary>
/// Checks a response against the OpenAPI contracts: the operation must be documented,
/// the status code must be documented for it, and the body must match the documented
/// schema (or be empty when none is documented).
/// </summary>
public sealed class ContractValidator
{
    private readonly IReadOnlyDictionary<string, OpenApiContract> _contracts;
    private readonly bool _strictObjects;

    public ContractValidator(IEnumerable<OpenApiContract> contracts, bool strictObjects = true)
    {
        _contracts = contracts.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        _strictObjects = strictObjects;
    }

    public IReadOnlyCollection<OpenApiContract> Contracts => _contracts.Values.ToList();

    public bool Covers(string service) => _contracts.ContainsKey(service);

    /// <param name="service">Contract name to use; null searches all contracts by path.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Request path relative to the service base URL (query string ignored).</param>
    /// <param name="template">Route template, when known (exact operation match).</param>
    /// <param name="statusCode">Response status code.</param>
    /// <param name="contentType">Response Content-Type header.</param>
    /// <param name="body">Raw response body.</param>
    public ContractCheck Check(string? service, string method, string path, string? template,
        int statusCode, string? contentType, string? body)
    {
        method = method.ToUpperInvariant();
        var (contract, operation) = FindOperation(service, method, path, template);

        if (contract is null)
            return new ContractCheck(service, method, path, null, statusCode,
                new[] { service is null ? "no contract documents this path" : $"no contract is configured for service '{service}'" });

        if (operation is null)
            return new ContractCheck(contract.Name, method, path, null, statusCode,
                new[] { $"operation {method} {OpenApiContract.Normalize(path)} is not documented" });

        var violations = new List<string>();
        var documented = operation.FindResponse(statusCode);
        if (documented is null)
        {
            violations.Add($"status {statusCode} is not documented (documented: {string.Join(", ", operation.Operation.Responses.Keys)})");
            return new ContractCheck(contract.Name, method, path, operation.PathTemplate, statusCode, violations);
        }

        var schema = JsonSchemaFor(documented.Value.Response);
        var hasBody = !string.IsNullOrWhiteSpace(body);

        if (schema is null)
        {
            if (hasBody)
                violations.Add($"a response body is not documented for {documented.Value.Key}, got: {Truncate(body!)}");
        }
        else if (!hasBody)
        {
            violations.Add($"expected a JSON body ({SchemaName(schema)}), got an empty body");
        }
        else
        {
            if (contentType is not null && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                violations.Add($"Content-Type '{contentType}' is not JSON");

            try
            {
                using var json = JsonDocument.Parse(body!);
                var validator = new JsonSchemaValidator(_strictObjects, contract.ResolveSchema);
                violations.AddRange(validator.Validate(json.RootElement, schema));
            }
            catch (JsonException ex)
            {
                violations.Add($"body is not valid JSON ({ex.Message}): {Truncate(body!)}");
            }
        }

        return new ContractCheck(contract.Name, method, path, operation.PathTemplate, statusCode, violations);
    }

    private (OpenApiContract? Contract, ContractOperation? Operation) FindOperation(
        string? service, string method, string path, string? template)
    {
        if (service is not null)
        {
            return _contracts.TryGetValue(service, out var contract)
                ? (contract, contract.Find(method, path, template))
                : (null, null);
        }

        var candidates = _contracts.Values
            .Select(c => (Contract: c, Operation: c.Find(method, path, template)))
            .Where(x => x.Operation is not null)
            .OrderByDescending(x => x.Operation!.LiteralSegments)
            .ToList();
        return candidates.Count > 0 ? (candidates[0].Contract, candidates[0].Operation) : (null, null);
    }

    private static OpenApiSchema? JsonSchemaFor(OpenApiResponse response)
        => response.Content
            .Where(c => c.Key.Contains("json", StringComparison.OrdinalIgnoreCase) || c.Key == "*/*")
            .Select(c => c.Value.Schema)
            .FirstOrDefault(s => s is not null);

    private static string SchemaName(OpenApiSchema schema)
        => schema.Reference?.Id ?? (schema.Items?.Reference?.Id is { } item ? $"array of {item}" : schema.Type ?? "schema");

    private static string Truncate(string text)
    {
        var singleLine = text.ReplaceLineEndings(" ");
        return singleLine.Length <= 200 ? singleLine : singleLine[..197] + "...";
    }
}
