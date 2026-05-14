namespace ApiTestFramework.Core.Auth;

/// <summary>
/// Per-test token holder using <see cref="AsyncLocal{T}"/> so that each async test
/// execution flow carries its own Bearer token without interfering with other tests
/// running in parallel.
///
/// Usage:
///   TestTokenContext.SetToken(myToken);     // in test [SetUp] or inline
///   TestTokenContext.Clear();               // in test [TearDown]
///
/// <see cref="ApiClient"/> prefers this over the global <see cref="TokenProvider"/>:
///   1. If <see cref="TestTokenContext.HasToken"/> → use per-test token
///   2. Else if <see cref="TokenProvider.HasToken"/>  → use global token (backwards-compat)
///   3. Else → no token injected
/// </summary>
public static class TestTokenContext
{
    private static readonly AsyncLocal<string?> _token = new();

    /// <summary>Gets the token for the current async execution context.</summary>
    public static string? Token => _token.Value;

    /// <summary>Returns true if a per-test token is set in the current context.</summary>
    public static bool HasToken => !string.IsNullOrEmpty(_token.Value);

    /// <summary>Sets a token scoped to the current async execution context.</summary>
    public static void SetToken(string token) => _token.Value = token;

    /// <summary>Clears the token for the current async execution context.</summary>
    public static void Clear() => _token.Value = null;
}
