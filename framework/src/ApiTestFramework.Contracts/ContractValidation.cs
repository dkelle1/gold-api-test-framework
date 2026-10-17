using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Logging;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Serilog;

namespace ApiTestFramework.Contracts;

public enum ContractValidationMode
{
    /// <summary>No automatic checks; <c>ShouldMatchContract()</c> still works.</summary>
    Off,

    /// <summary>Every response is checked; violations are logged and attached to Allure.</summary>
    Warn,

    /// <summary>Every response is checked; a violation fails the test at that request.</summary>
    Fail
}

/// <summary>Binds the "ContractValidation" section of appsettings.test.json.</summary>
public sealed class ContractValidationOptions
{
    public ContractValidationMode Mode { get; set; } = ContractValidationMode.Off;

    /// <summary>
    /// ApiClient name (the Services key unless a Name is configured) → OpenAPI file, relative to
    /// the test assembly directory. Responses of services without an entry are not checked.
    /// </summary>
    public Dictionary<string, string> Specs { get; set; } = new();

    /// <summary>Reject undeclared properties and missing non-nullable properties (default true).</summary>
    public bool StrictObjects { get; set; } = true;

    /// <summary>Where <see cref="ContractValidation.WriteCoverageReport"/> writes, relative to the test assembly directory.</summary>
    public string ReportDirectory { get; set; } = "contract-coverage";
}

/// <summary>
/// Entry point: load the contracts once in the global setup, then every ApiClient response is
/// checked (Mode Warn/Fail) and <c>ShouldMatchContract()</c> is available in tests.
/// <code>
/// ContractValidation.EnableFromConfiguration();          // [OneTimeSetUp]
/// ContractValidation.WriteCoverageReport();              // [OneTimeTearDown]
/// </code>
/// </summary>
public static class ContractValidation
{
    private static readonly object Lock = new();
    private static ContractResponseValidator? _registered;

    public static ContractValidator? Validator { get; private set; }

    public static ContractValidationOptions Options { get; private set; } = new();

    public static ContractCoverage Coverage { get; private set; } = new();

    public static bool IsEnabled => Validator is not null;

    /// <summary>
    /// Enables validation from the "ContractValidation" configuration section. Returns false (and
    /// does nothing) when the section is missing or lists no specs.
    /// </summary>
    public static bool EnableFromConfiguration(IConfiguration? configuration = null)
    {
        var options = new ContractValidationOptions();
        (configuration ?? ApiTestFramework.Core.Configuration.ConfigurationProvider.Configuration).GetSection("ContractValidation").Bind(options);
        if (options.Specs.Count == 0)
            return false;

        Enable(options);
        return true;
    }

    /// <param name="options">Mode, specs and strictness.</param>
    /// <param name="baseDirectory">Base for relative spec paths (default: the test assembly directory).</param>
    public static void Enable(ContractValidationOptions options, string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var contracts = options.Specs
            .Select(spec => OpenApiContract.Load(spec.Key, Path.GetFullPath(Path.Combine(root, spec.Value))))
            .ToList();

        lock (Lock)
        {
            Disable();
            Options = options;
            Validator = new ContractValidator(contracts, options.StrictObjects);
            Coverage = new ContractCoverage();
            if (options.Mode != ContractValidationMode.Off)
            {
                _registered = new ContractResponseValidator();
                ResponseValidators.Register(_registered);
            }
        }

        Log.Information("Contract validation {Mode}: {Contracts}", options.Mode,
            string.Join(", ", contracts.Select(c => $"{c.Name} ({c.Operations.Count} operations)")));
    }

    public static void Disable()
    {
        lock (Lock)
        {
            if (_registered is not null)
            {
                var others = ResponseValidators.All.Where(v => v != _registered).ToList();
                ResponseValidators.Clear();
                others.ForEach(ResponseValidators.Register);
                _registered = null;
            }
            Validator = null;
        }
    }

    /// <summary>Writes contract-coverage.md / .json; returns the Markdown path, or null when disabled.</summary>
    public static string? WriteCoverageReport(string? directory = null)
    {
        if (Validator is null)
            return null;

        var target = directory ?? Path.Combine(AppContext.BaseDirectory, Options.ReportDirectory);
        var path = Coverage.WriteReport(Validator.Contracts, target);
        Log.Information("Contract coverage report: {Path} ({Violations} violation(s))", path, Coverage.TotalViolations);
        return path;
    }

    internal static void Report(ContractCheck check, RestSharp.RestResponse response, bool fail)
    {
        Coverage.Record(check);
        if (check.IsValid)
            return;

        var message = check.Describe();
        AllureRequestLogger.AttachFailureSummary(response, message);
        if (fail)
            throw new AssertionException(message);
        Log.Warning("{ContractViolation}", message);
    }

    /// <summary>Hooks contract checks into every ApiClient response.</summary>
    private sealed class ContractResponseValidator : IResponseValidator
    {
        public void Validate(ResponseValidationContext context)
        {
            var validator = Validator;
            if (validator is null || context.Response.StatusCode == 0 || !validator.Covers(context.ServiceName))
                return;

            var check = validator.Check(context.ServiceName, context.Request.Method.ToString(),
                context.Request.Resource, context.PathTemplate, (int)context.Response.StatusCode,
                context.Response.ContentType, context.Response.Content);

            Report(check, context.Response, fail: Options.Mode == ContractValidationMode.Fail);
        }
    }
}
