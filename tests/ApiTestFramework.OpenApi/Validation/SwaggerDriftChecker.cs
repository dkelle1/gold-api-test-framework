using ApiTestFramework.OpenApi.Loader;
using ApiTestFramework.OpenApi.Models;
using Microsoft.OpenApi.Models;

namespace ApiTestFramework.OpenApi.Validation;

/// <summary>
/// Result of a drift comparison: hard drift breaks generated code / clients
/// and should fail CI; warnings are metadata differences (nullability,
/// required flags) that deserve a look but not a red build.
/// </summary>
public record DriftReport(IReadOnlyList<string> Drift, IReadOnlyList<string> Warnings)
{
    public bool HasDrift => Drift.Count > 0;
}

/// <summary>
/// Structurally compares the committed (offline) swagger against the live one
/// served by a running service. Textual formatting differences are ignored —
/// only the shape that generated code depends on is compared:
/// endpoint set (method + path + operationId + request schema) and, for every
/// request-reachable schema, its property names, kinds and types.
/// </summary>
public static class SwaggerDriftChecker
{
    public static DriftReport Compare(OpenApiDocument committed, OpenApiDocument live)
    {
        var drift = new List<string>();
        var warnings = new List<string>();

        CompareEndpoints(committed, live, drift);
        CompareRequestSchemas(committed, live, drift, warnings);

        return new DriftReport(drift, warnings);
    }

    private static void CompareEndpoints(
        OpenApiDocument committed, OpenApiDocument live, List<string> drift)
    {
        var committedEndpoints = OpenApiSpecLoader.GetEndpoints(committed)
            .ToDictionary(e => $"{e.Method} {e.Path}");
        var liveEndpoints = OpenApiSpecLoader.GetEndpoints(live)
            .ToDictionary(e => $"{e.Method} {e.Path}");

        foreach (var key in committedEndpoints.Keys.Except(liveEndpoints.Keys))
            drift.Add($"Endpoint removed from service: {key}");

        foreach (var key in liveEndpoints.Keys.Except(committedEndpoints.Keys))
            drift.Add($"New endpoint in service (not in committed swagger): {key}");

        foreach (var key in committedEndpoints.Keys.Intersect(liveEndpoints.Keys))
        {
            var c = committedEndpoints[key];
            var l = liveEndpoints[key];

            if (!string.Equals(c.RequestBodySchema, l.RequestBodySchema, StringComparison.Ordinal))
                drift.Add($"{key}: request body schema changed " +
                          $"'{c.RequestBodySchema ?? "(none)"}' -> '{l.RequestBodySchema ?? "(none)"}'");

            if (!string.Equals(c.OperationId, l.OperationId, StringComparison.Ordinal))
                drift.Add($"{key}: operationId changed '{c.OperationId}' -> '{l.OperationId}'");
        }
    }

    private static void CompareRequestSchemas(
        OpenApiDocument committed, OpenApiDocument live,
        List<string> drift, List<string> warnings)
    {
        var committedSchemas = SchemaExtractor.GetRequestSchemas(committed)
            .ToDictionary(s => s.Name);
        var liveSchemas = SchemaExtractor.GetRequestSchemas(live)
            .ToDictionary(s => s.Name);

        foreach (var name in committedSchemas.Keys.Except(liveSchemas.Keys))
            drift.Add($"Schema removed from service: {name}");

        foreach (var name in liveSchemas.Keys.Except(committedSchemas.Keys))
            drift.Add($"New schema in service (not in committed swagger): {name}");

        foreach (var name in committedSchemas.Keys.Intersect(liveSchemas.Keys))
        {
            var c = committedSchemas[name];
            var l = liveSchemas[name];

            if (c.IsEnum || l.IsEnum)
            {
                if (c.IsEnum != l.IsEnum)
                    drift.Add($"Schema {name}: enum/object kind mismatch");
                // Enum values are compared only by count — the same C# enum is
                // serialized as names in the hand-maintained swagger and as
                // integers by Swashbuckle, so value-level comparison is noise.
                else if (c.EnumValues.Count != l.EnumValues.Count)
                    drift.Add($"Schema {name}: enum value count changed {c.EnumValues.Count} -> {l.EnumValues.Count}");
                continue;
            }

            var cProps = c.Properties.ToDictionary(p => p.JsonName, StringComparer.OrdinalIgnoreCase);
            var lProps = l.Properties.ToDictionary(p => p.JsonName, StringComparer.OrdinalIgnoreCase);

            foreach (var prop in cProps.Keys.Except(lProps.Keys, StringComparer.OrdinalIgnoreCase))
                drift.Add($"Schema {name}: property '{prop}' removed from service");

            foreach (var prop in lProps.Keys.Except(cProps.Keys, StringComparer.OrdinalIgnoreCase))
                drift.Add($"Schema {name}: new property '{prop}' in service");

            foreach (var prop in cProps.Keys.Intersect(lProps.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var cp = cProps[prop];
                var lp = lProps[prop];

                if (cp.Kind != lp.Kind)
                    drift.Add($"Schema {name}.{prop}: kind changed {cp.Kind} -> {lp.Kind}");
                else if (!string.Equals(cp.ClrType, lp.ClrType, StringComparison.Ordinal))
                    drift.Add($"Schema {name}.{prop}: type changed {cp.ClrType} -> {lp.ClrType}");

                // Nullability/required differences are frequent false positives
                // (Swashbuckle emits them only with validation attributes) —
                // report as warnings, not drift.
                if (cp.Nullable != lp.Nullable)
                    warnings.Add($"Schema {name}.{prop}: nullable {cp.Nullable} -> {lp.Nullable}");
                if (cp.Required != lp.Required)
                    warnings.Add($"Schema {name}.{prop}: required {cp.Required} -> {lp.Required}");
            }
        }
    }
}
