namespace ApiTestFramework.Core.Configuration;

public class ServiceConfig
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Named auth client (<c>Authentication:Clients:&lt;AuthClient&gt;</c>) used for this service's
    /// requests. Empty: the default <c>Authentication</c> settings. Services sharing a client
    /// share its provider, and with it its cached token.
    /// </summary>
    public string AuthClient { get; set; } = string.Empty;
}

/// <summary>
/// A named test identity, typically registered by the consumer's global setup. Its Bearer token is stored
/// in TokenProvider under Name and can be used per request via AsIdentity(Name).
/// </summary>
public class IdentityConfig
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
}

/// <summary>
/// Binds appsettings.test.json (+ appsettings.test.{TEST_ENVIRONMENT}.json and
/// TEST_-prefixed environment variables). The framework knows no concrete
/// service — everything product-specific lives in <see cref="Services"/>.
/// </summary>
public class TestConfiguration
{
    public List<IdentityConfig> Identities { get; set; } = new();

    /// <summary>
    /// Service registry: every entry gets a named ApiClient registered
    /// automatically (resolve via <c>ContainerProvider.ResolveNamed&lt;ApiClient&gt;(key)</c>).
    /// Override per environment with e.g. <c>TEST_Services__OrderService__BaseUrl</c>.
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
