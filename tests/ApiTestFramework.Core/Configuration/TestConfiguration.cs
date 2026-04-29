namespace ApiTestFramework.Core.Configuration;

public class ServiceConfig
{
    public string BaseUrl { get; set; } = string.Empty;
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
}
