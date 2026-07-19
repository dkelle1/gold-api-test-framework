namespace ApiTestFramework.Core.Configuration;

public class ServiceConfig
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// A named test identity registered during GlobalSetup. Its Bearer token is stored
/// in TokenProvider under Name and can be used per request via AsIdentity(Name).
/// </summary>
public class IdentityConfig
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
}

public class TestConfiguration
{
    public ServiceConfig ProductService { get; set; } = new();
    public ServiceConfig OrderService { get; set; } = new();
    public ServiceConfig AuthService { get; set; } = new();
    public List<IdentityConfig> Identities { get; set; } = new();
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 0;
    public string Environment { get; set; } = "Development";
}
