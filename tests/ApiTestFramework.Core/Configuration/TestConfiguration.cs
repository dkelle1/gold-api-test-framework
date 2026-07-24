namespace ApiTestFramework.Core.Configuration;

public class ServiceConfig
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}

public class InfrastructureConfig
{
    /// <summary>Auto | External | TestContainers — see ApiTestFramework.Infrastructure.</summary>
    public string Mode { get; set; } = "Auto";
}

public class TestConfiguration
{
    public ServiceConfig ProductService { get; set; } = new();
    public ServiceConfig OrderService { get; set; } = new();
    public ServiceConfig AuthService { get; set; } = new();
    public InfrastructureConfig Infrastructure { get; set; } = new();
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 0;
    public string Environment { get; set; } = "Development";
}
