using ApiTestFramework.OpenApi.Generator;
using ApiTestFramework.OpenApi.Loader;

namespace ApiTestFramework.OpenApi.Cli;

/// <summary>
/// CLI entry-point for generating service step classes and DTO files from OpenAPI specs.
///
/// Modes
/// ─────
/// --mode steps  (default)  Generate a *ServiceSteps.cs
/// --mode dto               Generate a DTO source file from components/schemas
/// --mode tests             Generate verb-specific NUnit test stubs
///
/// Steps mode:
///   generate-steps --swagger &lt;path&gt; --service &lt;Name&gt; --dto &lt;DtoType&gt;
///                  --ns &lt;TargetNs&gt; --dto-ns &lt;DtoNs&gt; [--out &lt;file&gt;]
///
/// DTO mode:
///   generate-steps --mode dto --swagger &lt;path&gt; --ns &lt;TargetNs&gt;
///                  [--skip &lt;Schema1,Schema2&gt;] [--common-ns &lt;CommonNs&gt;]
///                  [--out &lt;file&gt;]
///
/// Tests mode:
///   generate-steps --mode tests --swagger &lt;path&gt; --service &lt;Name&gt;
///                  --ns &lt;TargetNs&gt; [--out &lt;file&gt;]
///
/// Examples:
///   generate-steps --swagger product-swagger.json --service Product --dto Product
///                  --ns ApiTestFramework.Steps.ServiceSteps
///                  --dto-ns ApiTestFramework.Clients.ProductService
///
///   generate-steps --mode dto --swagger product-swagger.json
///                  --ns ApiTestFramework.Clients.ProductService
///                  --skip AuditInfo --common-ns ApiTestFramework.Clients.Common
///                  --out ProductDtos.g.cs
///
///   generate-steps --mode tests --swagger product-swagger.json
///                  --service Product --ns ApiTestFramework.Tests.Generated
///                  --out ProductGeneratedTests.cs
/// </summary>
internal class Program
{
    private static int Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts is null)
        {
            PrintHelp();
            return 1;
        }

        try
        {
            Console.WriteLine($"Loading swagger: {opts.SwaggerPath}");
            var doc = OpenApiSpecLoader.LoadFromFile(opts.SwaggerPath);

            string source;
            string defaultOut;

            if (opts.Mode == "dto")
            {
                var skip = opts.SkipSchemas is not null
                    ? new HashSet<string>(opts.SkipSchemas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    : null;

                source = DtoGenerator.GenerateDtoFile(
                    doc,
                    opts.Namespace,
                    skip,
                    opts.CommonNamespace);

                defaultOut = Path.GetFileNameWithoutExtension(opts.SwaggerPath)
                    .Replace("-swagger", "Dtos")
                    .Replace("-", "") + ".g.cs";
            }
            else
            {
                var endpoints = OpenApiSpecLoader.GetEndpoints(doc);
                Console.WriteLine($"  Found {endpoints.Count} endpoint(s).");

                if (opts.Mode == "tests")
                {
                    var cases = TestCaseScaffolder.GenerateTestCases(endpoints, opts.ServiceName!);
                    Console.WriteLine($"  Generated {cases.Count} test case definition(s).");
                    source = TestCaseScaffolder.GenerateCSharpTestClass(cases, opts.Namespace);
                    defaultOut = $"{opts.ServiceName}GeneratedTests.g.cs";
                }
                else
                {
                    source = StepsGenerator.GenerateStepsClass(
                        endpoints,
                        opts.ServiceName!,
                        opts.ResponseDto!,
                        opts.Namespace,
                        opts.DtoNamespace!);

                    defaultOut = $"{opts.ServiceName}ServiceSteps.g.cs";
                }
            }

            var outPath = opts.OutputPath ?? defaultOut;

            var dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(outPath, source);
            Console.WriteLine($"Generated: {outPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    // ── arg parsing ────────────────────────────────────────────────────────────

    private record Options(
        string SwaggerPath,
        string Namespace,
        string Mode,
        // Steps-mode only
        string? ServiceName,
        string? ResponseDto,
        string? DtoNamespace,
        // DTO-mode only
        string? SkipSchemas,
        string? CommonNamespace,
        // Shared
        string? OutputPath);

    private static Options? ParseArgs(string[] args)
    {
        string? swagger = null, ns = null, mode = "steps",
                service = null, dto = null, dtoNs = null,
                skip = null, commonNs = null, output = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length && args[i].StartsWith("--")) break;
            switch (args[i])
            {
                case "--swagger":   swagger  = args[++i]; break;
                case "--ns":        ns       = args[++i]; break;
                case "--mode":      mode     = args[++i]; break;
                case "--service":   service  = args[++i]; break;
                case "--dto":       dto      = args[++i]; break;
                case "--dto-ns":    dtoNs    = args[++i]; break;
                case "--skip":      skip     = args[++i]; break;
                case "--common-ns": commonNs = args[++i]; break;
                case "--out":       output   = args[++i]; break;
            }
        }

        if (swagger is null || ns is null) return null;

        if (mode == "dto")
            return new Options(swagger, ns, mode, null, null, null, skip, commonNs, output);

        if (mode == "tests")
        {
            if (service is null) return null;
            return new Options(swagger, ns, mode, service, null, null, null, null, output);
        }

        // Steps mode — service, dto, dto-ns required
        if (service is null || dto is null || dtoNs is null) return null;
        return new Options(swagger, ns, mode, service, dto, dtoNs, null, null, output);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            generate-steps — generates *ServiceSteps.cs, DTO files, or NUnit test stubs from an OpenAPI swagger.json

            Steps mode (default):
              generate-steps --swagger <path>   Path to swagger.json
                             --service <name>   Service name, e.g. Product
                             --dto     <type>   Response DTO type, e.g. Product
                             --ns      <ns>     Target namespace, e.g. ApiTestFramework.Steps.ServiceSteps
                             --dto-ns  <ns>     DTO namespace, e.g. ApiTestFramework.Clients.ProductService
                             [--out    <file>]  Output file (default: {Service}ServiceSteps.g.cs)

            DTO mode:
              generate-steps --mode dto
                             --swagger <path>   Path to swagger.json
                             --ns      <ns>     Target namespace, e.g. ApiTestFramework.Clients.ProductService
                             [--skip   <list>]  Comma-separated schema names to skip (e.g. AuditInfo)
                             [--common-ns <ns>] Namespace for skipped/shared types (adds a using directive)
                             [--out    <file>]  Output file (default: derived from swagger filename)

                        Tests mode:
                            generate-steps --mode tests
                                                         --swagger <path>   Path to swagger.json
                                                         --service <name>   Service name, e.g. Product
                                                         --ns      <ns>     Target namespace, e.g. ApiTestFramework.Tests.Generated
                                                         [--out    <file>]  Output file (default: {Service}GeneratedTests.g.cs)
            """);
    }
}
