using System.Net;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.AuthService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
using ApiTestFramework.Steps.Auth;
using ApiTestFramework.Steps.DataGenerators;
using RestSharp;
using Serilog;

namespace ApiTestFramework.Steps.ServiceSteps;

/// <summary>
/// Step class for AuthService operations.
/// Provides methods to register users and obtain Bearer tokens.
/// </summary>
public class AuthServiceSteps
{
    private readonly ApiClient _client;
    private readonly ILogger _logger;

    public AuthServiceSteps()
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("AuthService");
        _logger = Log.ForContext<AuthServiceSteps>();
    }

    /// <summary>
    /// Registers a new user with random data and returns the auth response (including token).
    /// </summary>
    [AllureStep("Register a new user with random data")]
    public async Task<AuthResponse> RegisterUserAsync()
    {
        var request = AuthDataGenerator.GenerateRegisterRequest();
        return await RegisterUserAsync(request);
    }

    /// <summary>
    /// Registers a new user with a specific request.
    /// </summary>
    [AllureStep("Register a new user")]
    public async Task<AuthResponse> RegisterUserAsync(RegisterRequest registerRequest)
    {
        _logger.Information("Registering user: {Username}", registerRequest.Username);

        var response = await _client.SendAsync<AuthResponse>(
            RequestFactory.Post(AuthServiceRoutes.Register, registerRequest));

        response.ShouldHaveStatusCode(HttpStatusCode.Created);
        var authResponse = response.ShouldHaveData();

        _logger.Information("Registered user: {Username}, Token expires at: {ExpiresAt}",
            authResponse.User.Username, authResponse.Token.ExpiresAt);

        return authResponse;
    }

    /// <summary>
    /// Logs in with a specific request and returns the auth response.
    /// </summary>
    [AllureStep("Login user")]
    public async Task<AuthResponse> LoginAsync(LoginRequest loginRequest)
    {
        _logger.Information("Logging in user: {Username}", loginRequest.Username);

        var response = await _client.SendAsync<AuthResponse>(
            RequestFactory.Post(AuthServiceRoutes.Login, loginRequest));

        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var authResponse = response.ShouldHaveData();

        _logger.Information("Logged in user: {Username}, Token expires at: {ExpiresAt}",
            authResponse.User.Username, authResponse.Token.ExpiresAt);

        return authResponse;
    }

    /// <summary>
    /// Logs in with username/password and returns the auth response.
    /// </summary>
    [AllureStep("Login user: {username}")]
    public async Task<AuthResponse> LoginAsync(string username, string password)
    {
        var loginRequest = AuthDataGenerator.GenerateLoginRequest(username, password);
        return await LoginAsync(loginRequest);
    }

    /// <summary>
    /// Registers a random user and returns just the Bearer token.
    /// Convenience method for tests that only need the token.
    /// </summary>
    [AllureStep("Register user and get Bearer token")]
    public async Task<string> GetBearerTokenAsync()
    {
        var authResponse = await RegisterUserAsync();
        return authResponse.Token.AccessToken;
    }

    /// <summary>
    /// Attempts to register with a specific request and returns the raw response.
    /// Useful for negative test scenarios (duplicate user, validation errors).
    /// </summary>
    [AllureStep("Attempt to register user (raw response)")]
    public async Task<RestResponse<AuthResponse>> TryRegisterUserAsync(RegisterRequest registerRequest)
    {
        _logger.Information("Attempting to register user: {Username}", registerRequest.Username);

        var response = await _client.SendAsync<AuthResponse>(
            RequestFactory.Post(AuthServiceRoutes.Register, registerRequest));

        return response;
    }

    /// <summary>
    /// Attempts to login and returns the raw response.
    /// Useful for negative test scenarios (wrong password, non-existent user).
    /// </summary>
    [AllureStep("Attempt to login (raw response)")]
    public async Task<RestResponse<AuthResponse>> TryLoginAsync(LoginRequest loginRequest)
    {
        _logger.Information("Attempting to login user: {Username}", loginRequest.Username);

        var response = await _client.SendAsync<AuthResponse>(
            RequestFactory.Post(AuthServiceRoutes.Login, loginRequest));

        return response;
    }

    /// <summary>
    /// Registers a new random user with the given <paramref name="role"/> and returns a
    /// <see cref="UserScope"/> that overrides the Bearer token for the current async
    /// execution context until the scope is disposed.
    ///
    /// <code>
    /// await using var admin = await _authSteps.CreateUserScopeAsync(role: "Admin");
    /// // Requests made here use admin's token automatically.
    /// admin.AuthResponse.User.Role.Should().Be("Admin");
    /// </code>
    /// </summary>
    [AllureStep("Create user scope (role: {role})")]
    public Task<UserScope> CreateUserScopeAsync(string role = "User")
        => UserScope.CreateAsync(this, role);
}
