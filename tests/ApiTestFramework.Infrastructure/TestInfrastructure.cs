namespace ApiTestFramework.Infrastructure;

/// <summary>
/// How the suite obtains its system under test:
/// <list type="bullet">
/// <item><b>Auto</b> (default) — use the configured URLs if the services respond,
/// otherwise start everything with <see cref="TestStack"/>.</item>
/// <item><b>External</b> — always use the configured URLs (CI with docker compose).</item>
/// <item><b>TestContainers</b> — always start a fresh containerized stack.</item>
/// </list>
/// Selected via config key <c>Infrastructure:Mode</c>
/// (env var <c>TEST_Infrastructure__Mode</c>).
/// </summary>
public enum InfrastructureMode
{
    Auto,
    External,
    TestContainers
}

public static class TestInfrastructure
{
    public static InfrastructureMode ResolveMode(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "external" => InfrastructureMode.External,
            "testcontainers" or "containers" => InfrastructureMode.TestContainers,
            _ => InfrastructureMode.Auto
        };

    /// <summary>
    /// True when a service answers on its swagger endpoint within the timeout —
    /// the Auto-mode probe for an externally running stack.
    /// </summary>
    public static async Task<bool> IsReachableAsync(string baseUrl, TimeSpan timeout)
    {
        try
        {
            using var client = new HttpClient { Timeout = timeout };
            var response = await client.GetAsync($"{baseUrl.TrimEnd('/')}/swagger/v1/swagger.json");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Walks up from the test assembly directory to the repository root —
    /// the Docker build context for the service images.
    /// </summary>
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ApiTestFramework.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (ApiTestFramework.sln) — required to build service images with Testcontainers.");
    }
}
