using ApiTestFramework.OpenApi.Generator;
using ApiTestFramework.OpenApi.Loader;

namespace ApiTestFramework.OpenApi.Cli;

/// <summary>
/// CLI entry-point for generating *ServiceSteps.cs files from OpenAPI specs.
///
/// Usage:
///   generate-steps --swagger &lt;path-to-swagger.json&gt;
///                  --service  &lt;ServiceName&gt;        (e.g. Product)
///                  --dto      &lt;ResponseDtoName&gt;    (e.g. Product)
///                  --ns       &lt;TargetNamespace&gt;    (e.g. ApiTestFramework.Steps.ServiceSteps)
///                  --dto-ns   &lt;DtoNamespace&gt;       (e.g. ApiTestFramework.Clients.ProductService)
///                  --out      &lt;output-file.cs&gt;     (default: {ServiceName}ServiceSteps.g.cs)
///
/// Example (Product):
///   generate-steps --swagger tests/ApiTestFramework.Clients/swagger/product-swagger.json
///                  --service Product --dto Product
///                  --ns ApiTestFramework.Steps.ServiceSteps
///                  --dto-ns ApiTestFramework.Clients.ProductService
///
/// Example (Order):
///   generate-steps --swagger tests/ApiTestFramework.Clients/swagger/order-swagger.json
///                  --service Order --dto Order
///                  --ns ApiTestFramework.Steps.ServiceSteps
///                  --dto-ns ApiTestFramework.Clients.OrderService
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
            var doc       = OpenApiSpecLoader.LoadFromFile(opts.SwaggerPath);
            var endpoints = OpenApiSpecLoader.GetEndpoints(doc);
            Console.WriteLine($"  Found {endpoints.Count} endpoint(s).");

            var source = StepsGenerator.GenerateStepsClass(
                endpoints,
                opts.ServiceName,
                opts.ResponseDto,
                opts.Namespace,
                opts.DtoNamespace);

            var outPath = opts.OutputPath
                ?? $"{opts.ServiceName}ServiceSteps.g.cs";

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
        string ServiceName,
        string ResponseDto,
        string Namespace,
        string DtoNamespace,
        string? OutputPath);

    private static Options? ParseArgs(string[] args)
    {
        string? swagger = null, service = null, dto = null,
                ns = null, dtoNs = null, output = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--swagger": swagger = args[++i]; break;
                case "--service": service = args[++i]; break;
                case "--dto":     dto     = args[++i]; break;
                case "--ns":      ns      = args[++i]; break;
                case "--dto-ns":  dtoNs   = args[++i]; break;
                case "--out":     output  = args[++i]; break;
            }
        }

        if (swagger is null || service is null || dto is null || ns is null || dtoNs is null)
            return null;

        return new Options(swagger, service, dto, ns, dtoNs, output);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            generate-steps — generates a *ServiceSteps.cs from an OpenAPI swagger.json

            Usage:
              generate-steps --swagger <path>   Path to swagger.json
                             --service <name>   Service name, e.g. Product
                             --dto     <name>   Response DTO type, e.g. Product
                             --ns      <ns>     Target namespace, e.g. ApiTestFramework.Steps.ServiceSteps
                             --dto-ns  <ns>     DTO namespace, e.g. ApiTestFramework.Clients.ProductService
                             [--out    <file>]  Output file (default: {Service}ServiceSteps.g.cs)
            """);
    }
}
