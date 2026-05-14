using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.AuthService;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
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
    public async Task<RestResponse<AuthResponse>> RegisterUserAsync()
    {
        var request = AuthDataGenerator.GenerateRegisterRequest();
        return await RegisterUserAsync(request);
    }

    /// <summary>
    /// Registers a new random user with the given <paramref name="role"/> and returns the
    /// auth response. Use the returned value to enter a <see cref="ApiTestFramework.Steps.Auth.UserScope"/>
    /// synchronously in the calling test method so the token is set in the correct
    /// <see cref="System.Threading.AsyncLocal{T}"/> execution context:
    /// <code>
    /// var auth = await _authSteps.RegisterUserAsync("Admin");
    /// await using var scope = UserScope.FromAuthResponse(auth); // sync — correct context
    /// </code>
    /// </summary>
    [AllureStep("Register a new user with role: {role}")]
    public async Task<RestResponse<AuthResponse>> RegisterUserAsync(string role)
    {
        var request = AuthDataGenerator.GenerateRegisterRequest(role);
        return await RegisterUserAsync(request);
    }

    /// <summary>
    /// Registers a new user with a specific request.
    /// </summary>
    [AllureStep("Register a new user")]
    public async Task<RestResponse<AuthResponse>> RegisterUserAsync(RegisterRequest registerRequest)
    {
        _logger.Information("Registering user: {Username}", registerRequest.Username);

        var response = await _client.SendAsync<AuthResponse>(
            RequestFactory.Post(AuthServiceRoutes.Register, registerRequest));

        return response;
    }

    /// <summary>
    /// Logs in with a specific request and returns the auth response.
    /// </summary>
    [AllureStep("Login user")]
    public async Task<RestResponse<AuthResponse>> LoginAsync(LoginRequest loginRequest)
    {
        _logger.Information("Logging in user: {Username}", loginRequest.Username);

        var response = await _client.SendAsync<AuthResponse>(
            RequestFactory.Post(AuthServiceRoutes.Login, loginRequest));

        return response;
    }

    /// <summary>
    /// Logs in with username/password and returns the auth response.
    /// </summary>
    [AllureStep("Login user: {username}")]
    public async Task<RestResponse<AuthResponse>> LoginAsync(string username, string password)
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
        var response = await RegisterUserAsync();
        var token = response.Data?.Token?.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Token not returned by AuthService. Status: {response.StatusCode}");
        return token;
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
}
