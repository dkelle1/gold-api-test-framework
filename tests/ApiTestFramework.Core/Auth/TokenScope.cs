namespace ApiTestFramework.Core.Auth;

/// <summary>
/// Disposable scope that overrides the Bearer token for the current async execution
/// context via <see cref="TestTokenContext"/>, then restores the previous value on dispose.
///
/// Supports nesting: each scope saves the token that was active when it was created and
/// restores it when disposed.  Because <see cref="TestTokenContext"/> is backed by
/// <see cref="System.Threading.AsyncLocal{T}"/> the override is invisible to other
/// concurrent test contexts.
///
/// Usage:
/// <code>
/// // Scope a single test method to a specific token
/// using var scope = TokenScope.Use(adminToken);
/// var response = await client.SendAsync(RequestFactory.Delete("/api/products/1"));
/// response.ShouldHaveStatusCode(HttpStatusCode.NoContent);
/// // Previous token (or none) is automatically restored after the using block.
/// </code>
/// </summary>
public sealed class TokenScope : IDisposable
{
    private readonly string? _previous;
    private bool _disposed;

    private TokenScope(string token)
    {
        _previous = TestTokenContext.Token;
        TestTokenContext.SetToken(token);
    }

    /// <summary>
    /// Creates a new <see cref="TokenScope"/> that sets <paramref name="token"/> as the
    /// active Bearer token for the current async execution context.
    /// </summary>
    public static TokenScope Use(string token) => new(token);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_previous is not null)
            TestTokenContext.SetToken(_previous);
        else
            TestTokenContext.Clear();
    }
}
