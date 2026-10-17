using System.Text.Json;
using ApiTestFramework.Generator.Cli;
using ApiTestFramework.OpenApi.Generator;
using ApiTestFramework.OpenApi.Loader;

// ============================================================================
// ApiTestFramework.Generator.Cli
//
// Regenerates test artifacts of ONE consumer project from its offline
// swagger files, as described by a generator config file (generator.json):
//   1. Fluent test-data builders (*.g.cs, partial classes)
//   2. Test class scaffolds      (*.cs.txt, promoted to .cs by hand)
//
// Usage:
//   dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config <path/to/generator.json>
//
// All paths inside the config are relative to the config file, so the tool
// knows nothing about any particular product. See docs/framework/generators.md.
// ============================================================================

var configPath = ParseConfigPath(args);
if (configPath is null)
{
    Console.Error.WriteLine("Usage: ApiTestFramework.Generator.Cli --config <generator.json>");
    return 2;
}

configPath = Path.GetFullPath(configPath);
if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"Config file not found: {configPath}");
    return 2;
}

var config = JsonSerializer.Deserialize<GeneratorConfig>(
    File.ReadAllText(configPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })
    ?? throw new InvalidOperationException($"Empty generator config: {configPath}");

var errors = config.Validate();
if (errors.Count > 0)
{
    foreach (var error in errors)
        Console.Error.WriteLine($"Invalid config: {error}");
    return 2;
}

var baseDir = Path.GetDirectoryName(configPath)!;
string Resolve(string relative) => Path.GetFullPath(Path.Combine(baseDir, relative));
string Rel(string path) => Path.GetRelativePath(baseDir, path).Replace('\\', '/');

Console.WriteLine($"Config: {configPath}");

var totalBuilders = 0;
var totalTestCases = 0;

foreach (var service in config.Services)
{
    var swaggerPath = Resolve(Path.Combine(config.SwaggerDir, service.Swagger));
    if (!File.Exists(swaggerPath))
    {
        Console.Error.WriteLine($"[{service.Name}] SKIPPED — swagger file not found: {swaggerPath}");
        continue;
    }

    var document = OpenApiSpecLoader.LoadFromFile(swaggerPath);

    // --- 1. Builders from request schemas -----------------------------------
    if (config.Builders is { } builders)
    {
        var files = BuilderScaffolder.GenerateBuilders(
            SchemaExtractor.GetRequestSchemas(document),
            dtoNamespace: builders.DtoNamespace.ForService(service.Name),
            builderNamespace: builders.Namespace.ForService(service.Name),
            sourceName: Rel(swaggerPath),
            regenerateHint: config.RegenerateHint);

        var outDir = Resolve(builders.OutputDir.ForService(service.Name));
        Directory.CreateDirectory(outDir);

        // Remove stale generated files so deleted schemas disappear
        foreach (var stale in Directory.GetFiles(outDir, "*.g.cs"))
            File.Delete(stale);

        foreach (var (fileName, content) in files)
            File.WriteAllText(Path.Combine(outDir, fileName), content);

        Console.WriteLine($"[{service.Name}] {files.Count} builder(s) -> {Rel(outDir)}");
        totalBuilders += files.Count;
    }

    // --- 2. Test scaffolds ---------------------------------------------------
    if (config.Scaffolds is { } scaffolds)
    {
        var testCases = TestCaseScaffolder.GenerateTestCases(OpenApiSpecLoader.GetEndpoints(document), service.Name);
        var testClass = TestCaseScaffolder.GenerateCSharpTestClass(
            testCases, scaffolds.Namespace.ForService(service.Name));

        var outDir = Resolve(scaffolds.OutputDir.ForService(service.Name));
        Directory.CreateDirectory(outDir);
        // .cs.txt by default: scaffolds are templates, promoted to .cs by hand
        var scaffoldPath = Path.Combine(outDir, $"{service.Name}GeneratedTests{scaffolds.FileExtension}");
        File.WriteAllText(scaffoldPath, testClass);

        Console.WriteLine($"[{service.Name}] {testCases.Count} test case(s) -> {Rel(scaffoldPath)}");
        totalTestCases += testCases.Count;
    }
}

Console.WriteLine();
Console.WriteLine($"Done: {totalBuilders} builders, {totalTestCases} test cases scaffolded.");
return 0;

static string? ParseConfigPath(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is "--config" or "-c" && i + 1 < args.Length)
            return args[i + 1];
    }
    // Convenience: a generator.json in the current directory
    return File.Exists("generator.json") ? "generator.json" : null;
}
