namespace ApiTestFramework.Core.Configuration;

public class ServiceConfig
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}

public class TestConfiguration
{
    public ServiceConfig ProductService { get; set; } = new();
    public ServiceConfig OrderService { get; set; } = new();
    public ServiceConfig AuthService { get; set; } = new();

    /// <summary>
    /// Product-agnostic service registry: every entry gets a named ApiClient
    /// registered automatically (resolve via
    /// <c>ContainerProvider.ResolveNamed&lt;ApiClient&gt;(key)</c>).
    /// New products add services here instead of extending this class:
    /// <code>
    /// "Services": {
    ///   "InventoryService": { "BaseUrl": "http://localhost:6100" },
    ///   "BillingService":   { "BaseUrl": "http://localhost:6200" }
    /// }
    /// </code>
    /// </summary>
    public Dictionary<string, ServiceConfig> Services { get; set; } = new();

    /// <summary>Authentication strategy — see Auth/AuthenticationOptions.</summary>
    public Auth.AuthenticationOptions Authentication { get; set; } = new();

    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 0;
    public string Environment { get; set; } = "Development";
}
