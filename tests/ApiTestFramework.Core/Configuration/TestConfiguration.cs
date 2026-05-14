namespace ApiTestFramework.Core.Configuration;

public class ServiceConfig
{
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// Consul connection settings.  Populated from <c>appsettings.test.json</c> or the
/// <c>TEST_Consul__Address</c> environment variable.  When <see cref="Address"/> is
/// non-empty the framework tries to load service base-URLs from Consul KV
/// (<c>api-test-framework/Services/&lt;name&gt;/BaseUrl</c>); any value found in Consul
/// overrides the local JSON file.
/// </summary>
public class ConsulConfig
{
    /// <summary>HTTP address of the Consul agent, e.g. <c>http://localhost:8500</c>.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>KV prefix used by this framework. Default: <c>api-test-framework</c>.</summary>
    public string KeyPrefix { get; set; } = "api-test-framework";
}

public class TestConfiguration
{
    /// <summary>
    /// Keyed by service name, e.g. "AuthService", "ProductService", "OrderService".
    /// Add entries here and in appsettings.test.json to onboard new services — no C# code change required.
    /// </summary>
    public Dictionary<string, ServiceConfig> Services { get; set; } = new();
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 0;
    public string Environment { get; set; } = "Development";

    /// <summary>Consul connection settings. Leave <see cref="ConsulConfig.Address"/> empty to disable.</summary>
    public ConsulConfig Consul { get; set; } = new();
}
