using ApiTestFramework.Clients.AuthService;
using ApiTestFramework.Core.Auth;

namespace ApiTestFramework.Steps.Auth;

/// <summary>
/// Disposable scope that overrides the Bearer token for the current async execution
/// context for the lifetime of the scope.
///
/// On dispose the token context is restored to whatever it was before the scope was
/// entered (including "no token"), so scopes can be freely nested.
///
/// <b>Correct usage</b> — register the user first (async), then enter the scope
/// synchronously in the test method body so the token is set in the right
/// <see cref="System.Threading.AsyncLocal{T}"/> execution context:
/// <code>
/// var auth = await _authSteps.RegisterUserAsync("Admin");
/// await using var adminUser = UserScope.FromAuthResponse(auth); // sync call ← correct
/// var response = await _productSteps.DeleteProductAsync(product.Id);
/// response.ShouldHaveStatusCode(HttpStatusCode.NoContent);
/// adminUser.AuthResponse.User.Role.Should().Be("Admin");
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
    /// Enters a token scope using an <paramref name="authResponse"/> that was already
    /// obtained externally (e.g., from a previous login/register call).
    ///
    /// <para>
    /// <b>Important:</b> call this <i>synchronously</i> in the test method body — not
    /// inside an async helper — so that <see cref="TestTokenContext.SetToken"/> runs in
    /// the test's own <see cref="System.Threading.AsyncLocal{T}"/> execution context and
    /// the token is visible to all subsequent awaits in that test.
    /// </para>
    ///
    /// <code>
    /// var auth = await _authSteps.RegisterUserAsync("Admin");
    /// await using var scope = UserScope.FromAuthResponse(auth); // sync — correct context
    /// </code>
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
