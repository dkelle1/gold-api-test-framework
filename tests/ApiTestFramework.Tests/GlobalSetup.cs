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
        var registerRequest = new RegisterRequest
        {
            Username = "test_user_global",
            Email = "test_global@test.com",
            Password = "TestPass123!",
            Role = "User"
        };
        var response = await authClient.SendAsync<AuthResponse>(
            RequestFactory.Post(AuthServiceRoutes.Register, registerRequest));

        if (response.StatusCode != HttpStatusCode.Created || response.Data?.Token?.AccessToken == null)
        {
            throw new InvalidOperationException(
                $"Failed to acquire Bearer token. Status: {response.StatusCode}, Content: {response.Content}");
        }

        TokenProvider.SetToken(response.Data.Token.AccessToken);
        Log.Information("Bearer token acquired successfully for user: {User}", response.Data.User.Username);
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
