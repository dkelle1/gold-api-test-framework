using Microsoft.Extensions.Configuration;

namespace ApiTestFramework.Core.Configuration;

public static class ConfigurationProvider
{
    private static IConfiguration? _configuration;

    public static IConfiguration Configuration
    {
        get
        {
            if (_configuration == null)
            {
                var environment = System.Environment.GetEnvironmentVariable("TEST_ENVIRONMENT") ?? "Development";

                _configuration = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.test.json", optional: false, reloadOnChange: false)
                    .AddJsonFile($"appsettings.test.{environment}.json", optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables(prefix: "TEST_")
                    .Build();
            }

            return _configuration;
        }
    }

    public static TestConfiguration GetTestConfiguration()
    {
        var config = new TestConfiguration();
        Configuration.Bind(config);
        return config;
    }
}
