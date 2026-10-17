// ==========================================================================
// PLACEHOLDER DTOs — These will be replaced by NSwag-generated code.
// Run generate-clients.bat to regenerate from swagger/auth-swagger.json.
// Structure must match the nested AuthResponse returned by AuthService.
// ==========================================================================

namespace DemoShop.Clients.AuthService;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public TokenInfo Token { get; set; } = new();
    public UserProfile User { get; set; } = new();
}

public class TokenInfo
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public System.DateTimeOffset ExpiresAt { get; set; }
    public int ExpiresInSeconds { get; set; }
}

public class UserProfile
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

/// <summary>Successful response of POST /connect/token (OAuth2 client credentials).</summary>
public class TokenResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}

/// <summary>OAuth2 error body (RFC 6749 section 5.2) of POST /connect/token.</summary>
public class OAuthError
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("error_description")]
    public string ErrorDescription { get; set; } = string.Empty;
}
