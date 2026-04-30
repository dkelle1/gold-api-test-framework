using Microsoft.Extensions.Configuration;
using Winton.Extensions.Configuration.Consul;

namespace ApiTestFramework.Core.Configuration;

public static class ConfigurationProvider
{
    private static readonly Lazy<IConfiguration> _lazyConfiguration = new(BuildConfiguration);

    public static IConfiguration Configuration => _lazyConfiguration.Value;

    public static TestConfiguration GetTestConfiguration()
    {
        var config = new TestConfiguration();
        Configuration.Bind(config);
        return config;
    }

    // ── Build pipeline ───────────────────────────────────────────────────────

    private static IConfiguration BuildConfiguration()
    {
        var environment = System.Environment.GetEnvironmentVariable("TEST_ENVIRONMENT") ?? "Development";

        // ── Phase 1: local sources only (needed to discover the Consul address) ──
        var localBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.test.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.test.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "TEST_");

        var localConfig = localBuilder.Build();

        // ── Phase 2: if Consul is configured, add it on top ──────────────────
        var consulAddress = localConfig["Consul:Address"];
        var keyPrefix = localConfig["Consul:KeyPrefix"] ?? "api-test-framework";

        if (string.IsNullOrWhiteSpace(consulAddress))
        {
            // Consul not configured — return local config as-is
            return localConfig;
        }

        // Re-build with Consul layered after JSON so Consul values win.
        // "optional: true" means the test suite still works when Consul is
        // not reachable (e.g. local dev without Docker).
        var fullBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.test.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.test.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "TEST_")
            .AddConsul($"{keyPrefix}", options =>
            {
                options.ConsulConfigurationOptions = cfg =>
                    cfg.Address = new Uri(consulAddress);

                // Do not throw if Consul is unreachable — fall back to JSON values.
                options.Optional = true;
                options.ReloadOnChange = false;
            });

        return fullBuilder.Build();
    }
}
