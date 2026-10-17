using System.Text.Json;

namespace ApiTestFramework.Core.Auth;

/// <summary>
/// OAuth2 client-credentials flow (RFC 6749 §4.4) — the standard scheme for
/// machine-to-machine APIs behind IdentityServer, Auth0, Keycloak, Azure AD, etc.
///
/// Fetches an access token from the configured token endpoint on first use and
/// caches it until shortly before expiry (RefreshSkewSeconds), then refreshes
/// transparently. Thread-safe; the blocking fetch happens at most once per
/// token lifetime.
/// </summary>
public sealed class OAuth2ClientCredentialsProvider : IAuthenticationProvider
{
    private static readonly HttpClient HttpClient = new();

    private readonly OAuth2Options _options;
    private readonly string _headerName;
    private readonly string _scheme;
    private readonly object _lock = new();

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public OAuth2ClientCredentialsProvider(OAuth2Options options,
                                           string headerName = "Authorization",
                                           string scheme = "Bearer")
    {
        if (string.IsNullOrEmpty(options.TokenUrl))
            throw new ArgumentException(
                "Authentication:OAuth2:TokenUrl must be configured for OAuth2ClientCredentials mode.");
        _options = options;
        _headerName = headerName;
        _scheme = scheme;
    }

    public AuthenticationHeader? GetAuthenticationHeader()
    {
        lock (_lock)
        {
            if (_cachedToken is null || DateTimeOffset.UtcNow >= _expiresAt)
            {
                (_cachedToken, _expiresAt) = FetchToken();
            }
            return new AuthenticationHeader(_headerName, Schemes.Prefix(_scheme, _cachedToken));
        }
    }

    private (string Token, DateTimeOffset ExpiresAt) FetchToken()
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
        };
        if (!string.IsNullOrEmpty(_options.Scope)) form["scope"] = _options.Scope;
        if (!string.IsNullOrEmpty(_options.Audience)) form["audience"] = _options.Audience;

        using var response = HttpClient
            .PostAsync(_options.TokenUrl, new FormUrlEncodedContent(form))
            .GetAwaiter().GetResult();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"OAuth2 token request to {_options.TokenUrl} failed: " +
                $"HTTP {(int)response.StatusCode} — {content}");

        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        var token = root.TryGetProperty("access_token", out var tokenProp)
            ? tokenProp.GetString()
            : null;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                $"OAuth2 token response from {_options.TokenUrl} has no access_token: {content}");

        var expiresIn = root.TryGetProperty("expires_in", out var expProp)
                        && expProp.TryGetInt32(out var seconds)
            ? seconds
            : 3600;
        var expiresAt = DateTimeOffset.UtcNow
            .AddSeconds(Math.Max(expiresIn - _options.RefreshSkewSeconds, 10));

        return (token, expiresAt);
    }
}
