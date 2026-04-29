namespace ApiTestFramework.Core.Auth;

/// <summary>
/// Thread-safe singleton that stores the current Bearer token.
/// Acquired once during test suite setup and shared by all ApiClients.
/// </summary>
public static class TokenProvider
{
    private static string? _token;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the current Bearer token.
    /// </summary>
    public static string? Token
    {
        get { lock (_lock) { return _token; } }
    }

    /// <summary>
    /// Sets the Bearer token (typically called once during GlobalSetup).
    /// </summary>
    public static void SetToken(string token)
    {
        lock (_lock) { _token = token; }
    }

    /// <summary>
    /// Clears the stored token (called during teardown).
    /// </summary>
    public static void Clear()
    {
        lock (_lock) { _token = null; }
    }

    /// <summary>
    /// Returns true if a token is available.
    /// </summary>
    public static bool HasToken => !string.IsNullOrEmpty(Token);
}
