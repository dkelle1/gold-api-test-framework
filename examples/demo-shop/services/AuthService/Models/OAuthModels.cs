using System.Text.Json.Serialization;

namespace AuthService.Models;

/// <summary>A machine client registered in the "OAuthClients" configuration section.</summary>
public class OAuthClient
{
    public string ClientId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;

    /// <summary>Scopes the client may request; granted all of them when it asks for none.</summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>Services (JWT "aud") the client's tokens are valid for.</summary>
    public List<string> Audiences { get; set; } = new();
}

/// <summary>Form fields of the token request (RFC 6749 §4.4.2), sent as application/x-www-form-urlencoded.</summary>
public class TokenRequest
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; set; } = string.Empty;

    /// <summary>Alternatively sent with HTTP Basic authentication.</summary>
    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    /// <summary>Space-separated subset of the client's scopes.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>One of the client's audiences; all of them when omitted.</summary>
    [JsonPropertyName("audience")]
    public string? Audience { get; set; }
}

/// <summary>Successful token response (RFC 6749 §5.1).</summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}

/// <summary>Token error response (RFC 6749 §5.2).</summary>
public class OAuthError
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("error_description")]
    public string ErrorDescription { get; set; } = string.Empty;
}
