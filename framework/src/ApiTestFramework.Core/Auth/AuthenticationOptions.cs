namespace ApiTestFramework.Core.Auth;

/// <summary>
/// Binds the "Authentication" section of appsettings.test.json.
/// Selects and parameterizes the <see cref="IAuthenticationProvider"/> used
/// by the framework. All values can be overridden via environment variables
/// (TEST_Authentication__Token, TEST_Authentication__OAuth2__ClientSecret, ...)
/// so secrets never need to live in the repository.
/// </summary>
public class AuthenticationOptions
{
    /// <summary>
    /// SessionToken (default) — token acquired once during suite setup and
    ///                          stored in <see cref="TokenProvider"/>
    ///                          (e.g. a register/login flow against an auth service)
    /// StaticToken             — fixed token from <see cref="Token"/> / env var
    /// ApiKey                  — <see cref="ApiKey"/> sent in <see cref="HeaderName"/>
    /// OAuth2ClientCredentials — token fetched from <see cref="OAuth2"/> endpoint, cached
    /// None                    — no authentication header
    /// Custom                  — instantiate <see cref="CustomProviderType"/>
    /// </summary>
    public string Mode { get; set; } = "SessionToken";

    /// <summary>Header carrying the credential. Default: Authorization.</summary>
    public string HeaderName { get; set; } = "Authorization";

    /// <summary>Prefix for token-based modes ("Bearer", "Token", or "" for a raw value).</summary>
    public string Scheme { get; set; } = "Bearer";

    /// <summary>Token for StaticToken mode (prefer env: TEST_Authentication__Token).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Key for ApiKey mode (prefer env: TEST_Authentication__ApiKey).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Assembly-qualified type name of an <see cref="IAuthenticationProvider"/>
    /// implementation, used when Mode = "Custom".
    /// </summary>
    public string CustomProviderType { get; set; } = string.Empty;

    public OAuth2Options OAuth2 { get; set; } = new();
}

public class OAuth2Options
{
    public string TokenUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>Seconds before expiry at which the cached token is refreshed.</summary>
    public int RefreshSkewSeconds { get; set; } = 30;
}
