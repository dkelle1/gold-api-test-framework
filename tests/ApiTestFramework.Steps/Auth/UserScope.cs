using ApiTestFramework.Clients.AuthService;
using ApiTestFramework.Core.Auth;
using ApiTestFramework.Steps.DataGenerators;
using ApiTestFramework.Steps.ServiceSteps;

namespace ApiTestFramework.Steps.Auth;

/// <summary>
/// Disposable scope that registers a fresh user with a given role and overrides the
/// Bearer token for the current async execution context for the lifetime of the scope.
///
/// On dispose the token context is restored to whatever it was before the scope was
/// entered (including "no token"), so scopes can be freely nested.
///
/// Usage — create once and await:
/// <code>
/// // Implicit restore at end of using block
/// await using var adminUser = await UserScope.CreateAsync(_authSteps, role: "Admin");
/// var response = await _productSteps.DeleteProductAsync(product.Id);
/// response.ShouldHaveStatusCode(HttpStatusCode.NoContent);
///
/// // Access the registered user's details for assertions
/// adminUser.AuthResponse.User.Role.Should().Be("Admin");
/// </code>
///
/// Usage — explicit token (no registration):
/// <code>
/// using var scope = UserScope.FromToken(existingToken);
/// </code>
/// </summary>
public sealed class UserScope : IDisposable, IAsyncDisposable
{
    /// <summary>The full auth response from registering/logging-in the user.</summary>
    public AuthResponse AuthResponse { get; }

    private readonly TokenScope _tokenScope;

    private UserScope(AuthResponse authResponse)
    {
        AuthResponse = authResponse;
        _tokenScope = TokenScope.Use(authResponse.Token.AccessToken);
    }

    // ── Factory methods ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers a new random user with the given <paramref name="role"/> and enters a
    /// token scope for that user.
    /// </summary>
    /// <param name="authSteps">The <see cref="AuthServiceSteps"/> used for registration.</param>
    /// <param name="role">The role to assign. Defaults to <c>"User"</c>.</param>
    public static async Task<UserScope> CreateAsync(AuthServiceSteps authSteps, string role = "User")
    {
        var request = AuthDataGenerator.GenerateRegisterRequest(role);
        var auth = await authSteps.RegisterUserAsync(request);
        return new UserScope(auth);
    }

    /// <summary>
    /// Enters a token scope using an <paramref name="authResponse"/> that was already
    /// obtained externally (e.g., from a previous login call).
    /// </summary>
    public static UserScope FromAuthResponse(AuthResponse authResponse)
        => new(authResponse);

    // ── IDisposable / IAsyncDisposable ────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose() => _tokenScope.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
