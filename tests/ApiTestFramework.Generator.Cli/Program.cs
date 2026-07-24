using ApiTestFramework.OpenApi.Generator;
using ApiTestFramework.OpenApi.Loader;
using ApiTestFramework.OpenApi.Validation;

// ============================================================================
// ApiTestFramework.Generator.Cli
//
// Mode 1 — generate (default): regenerates test-framework artifacts from the
// offline swagger files:
//   1. Fluent test-data builders  -> tests/ApiTestFramework.Steps/Builders/<Service>/Generated/*.g.cs
//   2. Test class scaffolds       -> tests/ApiTestFramework.Tests/Generated/<Service>GeneratedTests.cs.txt
//
//   dotnet run --project tests/ApiTestFramework.Generator.Cli [repo-root]
//
// Mode 2 — check-drift: structurally compares the committed swagger files with
// live ones downloaded from running services (same file names in <live-dir>).
// Exits 1 on hard drift (endpoints/schemas/properties/types), 0 with warnings
// for soft differences (nullable/required flags). Used as a CI gate.
//
//   dotnet run --project tests/ApiTestFramework.Generator.Cli -- check-drift <live-dir> [repo-root]
//
// Typically invoked via scripts/regenerate-all.ps1 after refreshing swagger
// files (scripts/refresh-swagger.ps1) and NSwag clients (generate-clients.bat).
//
// Generated builders are partial classes: hand-written customizations belong in
// Builders/Custom/*.cs partial files and are never touched by this tool.
// ============================================================================

var services = new[]
{
    new ServiceSpec("AuthService", "auth-swagger.json"),
    new ServiceSpec("ProductService", "product-swagger.json"),
    new ServiceSpec("OrderService", "order-swagger.json")
};

if (args.Length > 0 && args[0] == "check-drift")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: check-drift <live-swagger-dir> [repo-root]");
        return 2;
    }

    var liveDir = Path.GetFullPath(args[1]);
    var driftRepoRoot = args.Length > 2 ? Path.GetFullPath(args[2]) : FindRepoRoot();
    var committedDir = Path.Combine(driftRepoRoot, "tests", "ApiTestFramework.Clients", "swagger");
    var anyDrift = false;

    foreach (var service in services)
    {
        var committedPath = Path.Combine(committedDir, service.SwaggerFile);
        var livePath = Path.Combine(liveDir, service.SwaggerFile);

        if (!File.Exists(livePath))
        {
            Console.Error.WriteLine($"[{service.Name}] SKIPPED — live swagger not found: {livePath}");
            continue;
        }

        var committedDoc = OpenApiSpecLoader.LoadFromFile(committedPath);
        var liveDoc = OpenApiSpecLoader.LoadFromFile(livePath);
        var report = SwaggerDriftChecker.Compare(committedDoc, liveDoc);

        foreach (var warning in report.Warnings)
            Console.WriteLine($"[{service.Name}] WARN  {warning}");

        if (report.HasDrift)
        {
            anyDrift = true;
            foreach (var item in report.Drift)
                Console.Error.WriteLine($"[{service.Name}] DRIFT {item}");
        }
        else
        {
            Console.WriteLine($"[{service.Name}] OK — committed swagger matches the live service" +
                              (report.Warnings.Count > 0 ? $" ({report.Warnings.Count} warning(s))" : ""));
        }
    }

    if (anyDrift)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("Swagger drift detected. Refresh + regenerate: scripts/regenerate-all.ps1 -RefreshSwagger -NSwag");
        return 1;
    }

    return 0;
}

var repoRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
Console.WriteLine($"Repo root: {repoRoot}");

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
        File.WriteAllText(Path.Combine(outDir, fileName), Normalize(content));

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
    File.WriteAllText(scaffoldPath, Normalize(testClass));

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

// LF regardless of the OS the generator ran on — keeps regeneration
// byte-identical so the CI "Verify Generated Code" diff gate stays reliable
static string Normalize(string content) => content.Replace("\r\n", "\n");

internal sealed record ServiceSpec(string Name, string SwaggerFile);
