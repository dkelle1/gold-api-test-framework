using Microsoft.Extensions.Configuration;

namespace ApiTestFramework.Core.Configuration;

public static class ConfigurationProvider
{
    private static readonly Lazy<IConfiguration> _lazyConfiguration = new(() =>
    {
        var environment = System.Environment.GetEnvironmentVariable("TEST_ENVIRONMENT") ?? "Development";
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.test.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.test.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "TEST_")
            .Build();
    });

    public static IConfiguration Configuration => _lazyConfiguration.Value;

    public static TestConfiguration GetTestConfiguration()
    {
        var config = new TestConfiguration();
        Configuration.Bind(config);
        return config;
    }
}
