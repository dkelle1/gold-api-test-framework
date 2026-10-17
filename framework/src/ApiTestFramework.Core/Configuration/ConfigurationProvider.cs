using Microsoft.Extensions.Configuration;

namespace ApiTestFramework.Core.Configuration;

/// <summary>
/// Test configuration, lowest to highest precedence: appsettings.test.json,
/// appsettings.test.{TEST_ENVIRONMENT}.json, then TEST_-prefixed environment variables —
/// which include the values from a .env file (see <see cref="DotEnv"/>; real environment
/// variables win over the file).
/// </summary>
public static class ConfigurationProvider
{
    private static IConfiguration? _configuration;

    public static IConfiguration Configuration
    {
        get
        {
            if (_configuration == null)
            {
                // .env first: it may set TEST_ENVIRONMENT and any TEST_* override below
                DotEnv.LoadDefault();

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
