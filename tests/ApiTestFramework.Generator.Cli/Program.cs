using ApiTestFramework.OpenApi.Generator;
using ApiTestFramework.OpenApi.Loader;

// ============================================================================
// ApiTestFramework.Generator.Cli
//
// Regenerates test-framework artifacts from the offline swagger files:
//   1. Fluent test-data builders  -> tests/ApiTestFramework.Steps/Builders/<Service>/Generated/*.g.cs
//   2. Test class scaffolds       -> tests/ApiTestFramework.Tests/Generated/<Service>GeneratedTests.cs.txt
//
// Usage:
//   dotnet run --project tests/ApiTestFramework.Generator.Cli [repo-root]
//
// Typically invoked via scripts/regenerate-all.ps1 after refreshing swagger
// files (scripts/refresh-swagger.ps1) and NSwag clients (generate-clients.bat).
//
// Generated builders are partial classes: hand-written customizations belong in
// Builders/Custom/*.cs partial files and are never touched by this tool.
// ============================================================================

var repoRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
Console.WriteLine($"Repo root: {repoRoot}");

var services = new[]
{
    new ServiceSpec("AuthService", "auth-swagger.json"),
    new ServiceSpec("ProductService", "product-swagger.json"),
    new ServiceSpec("OrderService", "order-swagger.json")
};

var swaggerDir = Path.Combine(repoRoot, "tests", "ApiTestFramework.Clients", "swagger");
var buildersRoot = Path.Combine(repoRoot, "tests", "ApiTestFramework.Steps", "Builders");
var scaffoldsDir = Path.Combine(repoRoot, "tests", "ApiTestFramework.Tests", "Generated");

var totalBuilders = 0;
var totalTestCases = 0;

foreach (var service in services)
{
    var swaggerPath = Path.Combine(swaggerDir, service.SwaggerFile);
    if (!File.Exists(swaggerPath))
    {
        Console.Error.WriteLine($"[{service.Name}] SKIPPED — swagger file not found: {swaggerPath}");
        continue;
    }

    var document = OpenApiSpecLoader.LoadFromFile(swaggerPath);

    // --- 1. Builders from request schemas -----------------------------------
    var schemas = SchemaExtractor.GetRequestSchemas(document);
    var files = BuilderScaffolder.GenerateBuilders(
        schemas,
        dtoNamespace: $"ApiTestFramework.Clients.{service.Name}",
        builderNamespace: $"ApiTestFramework.Steps.Builders.{service.Name}",
        sourceName: $"swagger/{service.SwaggerFile}");

    var outDir = Path.Combine(buildersRoot, service.Name, "Generated");
    Directory.CreateDirectory(outDir);

    // Remove stale generated files so deleted schemas disappear
    foreach (var stale in Directory.GetFiles(outDir, "*.g.cs"))
        File.Delete(stale);

    foreach (var (fileName, content) in files)
        File.WriteAllText(Path.Combine(outDir, fileName), content);

    Console.WriteLine($"[{service.Name}] {files.Count} builder(s) -> {Rel(repoRoot, outDir)}");
    totalBuilders += files.Count;

    // --- 2. Test scaffolds ---------------------------------------------------
    var endpoints = OpenApiSpecLoader.GetEndpoints(document);
    var testCases = TestCaseScaffolder.GenerateTestCases(endpoints, service.Name);
    var testClass = TestCaseScaffolder.GenerateCSharpTestClass(
        testCases, $"ApiTestFramework.Tests.{service.Name}");

    Directory.CreateDirectory(scaffoldsDir);
    // .cs.txt on purpose: scaffolds are templates, promoted to .cs by hand
    var scaffoldPath = Path.Combine(scaffoldsDir, $"{service.Name}GeneratedTests.cs.txt");
    File.WriteAllText(scaffoldPath, testClass);

    Console.WriteLine($"[{service.Name}] {testCases.Count} test case(s) -> {Rel(repoRoot, scaffoldPath)}");
    totalTestCases += testCases.Count;
}

Console.WriteLine();
Console.WriteLine($"Done: {totalBuilders} builders, {totalTestCases} test cases scaffolded.");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ApiTestFramework.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException(
        "Could not locate repo root (ApiTestFramework.sln). Pass it explicitly: dotnet run --project tests/ApiTestFramework.Generator.Cli -- <repo-root>");
}

static string Rel(string root, string path) =>
    Path.GetRelativePath(root, path).Replace('\\', '/');

internal sealed record ServiceSpec(string Name, string SwaggerFile);
