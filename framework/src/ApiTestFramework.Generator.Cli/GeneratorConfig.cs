namespace ApiTestFramework.Generator.Cli;

/// <summary>
/// Shape of generator.json. Paths are relative to the config file;
/// "{service}" in any template is replaced with the service name.
/// </summary>
public sealed class GeneratorConfig
{
    /// <summary>Directory holding the offline swagger files.</summary>
    public string SwaggerDir { get; set; } = "swagger";

    /// <summary>Shown in the header of generated builders ("Regenerate: ...").</summary>
    public string RegenerateHint { get; set; } = "ApiTestFramework.Generator.Cli";

    public List<ServiceEntry> Services { get; set; } = new();

    /// <summary>Omit to skip builder generation.</summary>
    public BuilderSettings? Builders { get; set; }

    /// <summary>Omit to skip test scaffold generation.</summary>
    public ScaffoldSettings? Scaffolds { get; set; }

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (Services.Count == 0)
            errors.Add("'services' must list at least one service.");
        foreach (var service in Services)
        {
            if (string.IsNullOrWhiteSpace(service.Name)) errors.Add("every service needs a 'name'.");
            if (string.IsNullOrWhiteSpace(service.Swagger)) errors.Add($"service '{service.Name}' needs a 'swagger' file.");
        }
        if (Builders is { } b && (string.IsNullOrWhiteSpace(b.OutputDir) || string.IsNullOrWhiteSpace(b.Namespace) || string.IsNullOrWhiteSpace(b.DtoNamespace)))
            errors.Add("'builders' needs 'outputDir', 'namespace' and 'dtoNamespace'.");
        if (Scaffolds is { } s && (string.IsNullOrWhiteSpace(s.OutputDir) || string.IsNullOrWhiteSpace(s.Namespace)))
            errors.Add("'scaffolds' needs 'outputDir' and 'namespace'.");
        if (Builders is null && Scaffolds is null)
            errors.Add("nothing to generate: configure 'builders' and/or 'scaffolds'.");
        return errors;
    }
}

public sealed class ServiceEntry
{
    /// <summary>Service name, e.g. "OrderService" — used in namespaces and file names.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Swagger file name inside <see cref="GeneratorConfig.SwaggerDir"/>.</summary>
    public string Swagger { get; set; } = string.Empty;
}

public sealed class BuilderSettings
{
    /// <summary>e.g. "MyProduct.Steps/Builders/{service}/Generated"</summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>e.g. "MyProduct.Steps.Builders.{service}"</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Namespace of the DTOs the builders construct, e.g. "MyProduct.Clients.{service}"</summary>
    public string DtoNamespace { get; set; } = string.Empty;
}

public sealed class ScaffoldSettings
{
    /// <summary>e.g. "MyProduct.Tests/Generated"</summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>e.g. "MyProduct.Tests.{service}"</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>".cs.txt" (default, not compiled) or ".cs".</summary>
    public string FileExtension { get; set; } = ".cs.txt";
}

internal static class TemplateExtensions
{
    public static string ForService(this string template, string serviceName)
        => template.Replace("{service}", serviceName);
}
