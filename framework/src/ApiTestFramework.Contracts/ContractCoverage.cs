using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ApiTestFramework.Contracts;

/// <summary>
/// Records which documented operation × status code pairs a test run exercised, and which
/// responses violated the contract. Written as Markdown + JSON at the end of the run.
/// </summary>
public sealed class ContractCoverage
{
    private readonly ConcurrentDictionary<(string Contract, string Operation, int Status), Counter> _hits = new();

    private sealed class Counter
    {
        public int Calls;
        public int Violations;
        public string? FirstViolation;
    }

    public void Record(ContractCheck check)
    {
        var operation = check.OperationTemplate is null ? $"{check.Method} {check.Path}" : $"{check.Method} {check.OperationTemplate}";
        var counter = _hits.GetOrAdd((check.Contract ?? "(none)", operation, check.StatusCode), _ => new Counter());
        Interlocked.Increment(ref counter.Calls);
        if (!check.IsValid)
        {
            Interlocked.Increment(ref counter.Violations);
            counter.FirstViolation ??= check.Violations[0];
        }
    }

    public int TotalViolations => _hits.Values.Sum(c => c.Violations);

    /// <summary>Writes contract-coverage.md and contract-coverage.json; returns the Markdown path.</summary>
    public string WriteReport(IEnumerable<OpenApiContract> contracts, string directory)
    {
        Directory.CreateDirectory(directory);
        var rows = BuildRows(contracts).ToList();

        var documented = rows.Where(r => r.Documented).ToList();
        var exercised = documented.Count(r => r.Calls > 0);
        var md = new StringBuilder();
        md.AppendLine("### Contract coverage");
        md.AppendLine();
        md.AppendLine($"Documented responses exercised: **{exercised} / {documented.Count}** · " +
                      $"responses checked: {rows.Sum(r => r.Calls)} · violations: **{rows.Sum(r => r.Violations)}**");
        md.AppendLine();
        md.AppendLine("| Contract | Operation | Status | Calls | Violations | Note |");
        md.AppendLine("|----------|-----------|-------:|------:|-----------:|------|");
        foreach (var row in rows.OrderBy(r => r.Contract).ThenBy(r => r.Operation).ThenBy(r => r.Status))
        {
            var note = !row.Documented ? "not documented"
                : row.Calls == 0 ? "not exercised"
                : row.FirstViolation ?? "";
            md.AppendLine($"| {row.Contract} | `{row.Operation}` | {row.Status} | {row.Calls} | {row.Violations} | {note.Replace("|", "\\|")} |");
        }

        var mdPath = Path.Combine(directory, "contract-coverage.md");
        File.WriteAllText(mdPath, md.ToString());
        File.WriteAllText(Path.Combine(directory, "contract-coverage.json"),
            JsonSerializer.Serialize(rows, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return mdPath;
    }

    private IEnumerable<CoverageRow> BuildRows(IEnumerable<OpenApiContract> contracts)
    {
        var seen = new HashSet<(string, string, int)>();
        foreach (var contract in contracts)
        {
            foreach (var operation in contract.Operations)
            {
                foreach (var key in operation.Operation.Responses.Keys)
                {
                    if (!int.TryParse(key, out var status))
                        continue; // ranges and "default" cannot be listed as a single row
                    var id = (contract.Name, $"{operation.Method} {operation.PathTemplate}", status);
                    seen.Add(id);
                    _hits.TryGetValue(id, out var counter);
                    yield return new CoverageRow(id.Item1, id.Item2, status, true,
                        counter?.Calls ?? 0, counter?.Violations ?? 0, counter?.FirstViolation);
                }
            }
        }

        foreach (var ((contract, operation, status), counter) in _hits)
        {
            if (!seen.Contains((contract, operation, status)))
                yield return new CoverageRow(contract, operation, status, false, counter.Calls, counter.Violations, counter.FirstViolation);
        }
    }

    public sealed record CoverageRow(string Contract, string Operation, int Status, bool Documented,
        int Calls, int Violations, string? FirstViolation);
}
