using System.Net;
using Autofac;
using ApiTestFramework.Clients.AuthService;
using ApiTestFramework.Core.Auth;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Configuration;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
using NUnit.Framework;
using Serilog;

namespace ApiTestFramework.Tests;

/// <summary>
/// Global setup fixture — runs once before all tests in the assembly.
/// Initializes Autofac container, logging, configuration, and acquires a Bearer token.
/// </summary>
[SetUpFixture]
public class GlobalSetup
{
    [OneTimeSetUp]
    public async Task RunBeforeAllTests()
    {
        // Initialize Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== Test Suite Starting ===");

        // Load configuration
        var config = ConfigurationProvider.GetTestConfiguration();
        Log.Information("Environment: {Env}", config.Environment);
        Log.Information("AuthService URL: {Url}", config.AuthService.BaseUrl);
        Log.Information("ProductService URL: {Url}", config.ProductService.BaseUrl);
        Log.Information("OrderService URL: {Url}", config.OrderService.BaseUrl);

        // Initialize Autofac
        ContainerProvider.Initialize(builder =>
        {
            builder.RegisterModule(new ApiClientModule(config));
        });

        Log.Information("=== Container initialized ===");

        // Acquire Bearer token via AuthService (direct call — no AllureStep context here)
        Log.Information("Acquiring Bearer token...");
        var authClient = ContainerProvider.ResolveNamed<ApiClient>("AuthService");
        var defaultToken = await RegisterUserAndGetTokenAsync(authClient, new RegisterRequest
        {
            Username = "test_user_global",
            Email = "test_global@test.com",
            Password = "TestPass123!",
            Role = "User"
        });
        TokenProvider.SetToken(defaultToken);
        Log.Information("Default Bearer token acquired successfully");

        // Register additional named identities (e.g. admin/user) so tests can send
        // individual requests with different permissions via AsIdentity(name)
        foreach (var identity in config.Identities)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var token = await RegisterUserAndGetTokenAsync(authClient, new RegisterRequest
            {
                Username = $"test_{identity.Name}_{suffix}",
                Email = $"test_{identity.Name}_{suffix}@test.com",
                Password = "TestPass123!",
                Role = identity.Role
            });
            TokenProvider.SetToken(identity.Name, token);
            Log.Information("Identity '{Identity}' registered with role {Role}", identity.Name, identity.Role);
        }
    }

    private static async Task<string> RegisterUserAndGetTokenAsync(ApiClient authClient, RegisterRequest registerRequest)
    {
        var response = await authClient.SendAsync<AuthResponse>(
            RequestFactory.Post(AuthServiceRoutes.Register, registerRequest));

        // Re-running the suite against a live stack yields 409 for fixed usernames — log in instead
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var loginResponse = await authClient.SendAsync<AuthResponse>(
                RequestFactory.Post(AuthServiceRoutes.Login, new LoginRequest
                {
                    Username = registerRequest.Username,
                    Password = registerRequest.Password
                }));

            if (loginResponse.StatusCode == HttpStatusCode.OK && loginResponse.Data?.Token?.AccessToken != null)
            {
                return loginResponse.Data.Token.AccessToken;
            }
        }

        if (response.StatusCode != HttpStatusCode.Created || response.Data?.Token?.AccessToken == null)
        {
            throw new InvalidOperationException(
                $"Failed to acquire Bearer token for user '{registerRequest.Username}'. " +
                $"Status: {response.StatusCode}, Content: {response.Content}");
        }

        return response.Data.Token.AccessToken;
    }

    [OneTimeTearDown]
    public void RunAfterAllTests()
    {
        Log.Information("=== Test Suite Finished ===");
        TokenProvider.Clear();
        ContainerProvider.Dispose();
        Log.CloseAndFlush();
    }
}
