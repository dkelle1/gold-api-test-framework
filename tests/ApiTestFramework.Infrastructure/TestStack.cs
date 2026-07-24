using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace ApiTestFramework.Infrastructure;

/// <summary>
/// Spins up the full system under test with Testcontainers:
/// SQL Server + Redis + the three microservices (images built from the
/// repository Dockerfiles), wired together on a private Docker network —
/// the same topology as docker-compose.yml, but owned by the test run.
/// <para>
/// Containers are labelled by Testcontainers' resource reaper (Ryuk), so
/// they are removed even if the test process dies. First start builds the
/// service images (~minutes); subsequent runs reuse the Docker layer cache.
/// </para>
/// </summary>
public sealed class TestStack : IAsyncDisposable
{
    private const string SaPassword = "ApiTest_Sa@2024";
    private const string JwtSecretKey = "ApiTestFramework_SuperSecretKey_ForDevelopment_2024_MinLength32!";
    private const int ServicePort = 8080;

    private readonly INetwork _network;
    private readonly MsSqlContainer _sqlServer;
    private readonly RedisContainer _redis;
    private readonly IFutureDockerImage _authImage;
    private readonly IFutureDockerImage _productImage;
    private readonly IFutureDockerImage _orderImage;
    private readonly IContainer _authService;
    private readonly IContainer _productService;
    private readonly IContainer _orderService;

    public string AuthServiceUrl => $"http://localhost:{_authService.GetMappedPublicPort(ServicePort)}";
    public string ProductServiceUrl => $"http://localhost:{_productService.GetMappedPublicPort(ServicePort)}";
    public string OrderServiceUrl => $"http://localhost:{_orderService.GetMappedPublicPort(ServicePort)}";

    private TestStack(string repoRoot)
    {
        _network = new NetworkBuilder().Build();

        _sqlServer = new MsSqlBuilder()
            .WithPassword(SaPassword)
            .WithNetwork(_network)
            .WithNetworkAliases("sqlserver")
            .Build();

        _redis = new RedisBuilder()
            .WithNetwork(_network)
            .WithNetworkAliases("redis")
            .Build();

        _authImage = BuildImage(repoRoot, "src/AuthService/Dockerfile");
        _productImage = BuildImage(repoRoot, "src/ProductService/Dockerfile");
        _orderImage = BuildImage(repoRoot, "src/OrderService/Dockerfile");

        _authService = ServiceContainer(_authImage, "auth-service", "AuthServiceDb")
            .WithEnvironment("Jwt__ExpirationMinutes", "60")
            .Build();

        _productService = ServiceContainer(_productImage, "product-service", "ProductServiceDb")
            .Build();

        _orderService = ServiceContainer(_orderImage, "order-service", "OrderServiceDb")
            .WithEnvironment("ProductServiceUrl", $"http://product-service:{ServicePort}")
            .Build();
    }

    /// <summary>
    /// Builds images and starts all containers. Infrastructure first, then the
    /// three services in parallel (they only talk to each other at request time).
    /// </summary>
    public static async Task<TestStack> StartAsync(string repoRoot, CancellationToken ct = default)
    {
        var stack = new TestStack(repoRoot);

        await Task.WhenAll(
            stack._sqlServer.StartAsync(ct),
            stack._redis.StartAsync(ct),
            stack._authImage.CreateAsync(ct),
            stack._productImage.CreateAsync(ct),
            stack._orderImage.CreateAsync(ct));

        await Task.WhenAll(
            stack._authService.StartAsync(ct),
            stack._productService.StartAsync(ct),
            stack._orderService.StartAsync(ct));

        return stack;
    }

    private static IFutureDockerImage BuildImage(string repoRoot, string dockerfile) =>
        new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(repoRoot)
            .WithDockerfile(dockerfile)
            .WithCleanUp(true)
            .Build();

    private ContainerBuilder ServiceContainer(IFutureDockerImage image, string alias, string database) =>
        new ContainerBuilder()
            .WithImage(image)
            .WithNetwork(_network)
            .WithNetworkAliases(alias)
            .WithPortBinding(ServicePort, assignRandomHostPort: true)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("ASPNETCORE_URLS", $"http://+:{ServicePort}")
            .WithEnvironment("ConnectionStrings__DefaultConnection",
                $"Server=sqlserver;Database={database};User Id=sa;Password={SaPassword};TrustServerCertificate=True")
            .WithEnvironment("Redis__ConnectionString", "redis:6379")
            .WithEnvironment("Jwt__SecretKey", JwtSecretKey)
            .WithEnvironment("Jwt__Issuer", "ApiTestFramework.AuthService")
            .WithEnvironment("Jwt__Audience", "ApiTestFramework.Services")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(ServicePort)
                    .ForPath("/swagger/v1/swagger.json")));

    public async ValueTask DisposeAsync()
    {
        // Reverse order of startup; each Dispose is independent so a failure
        // in one does not leak the others (Ryuk is the final safety net).
        foreach (var resource in new IAsyncDisposable[]
                 { _orderService, _productService, _authService, _redis, _sqlServer, _network })
        {
            try { await resource.DisposeAsync(); }
            catch { /* reaper cleans up leftovers */ }
        }
    }
}
