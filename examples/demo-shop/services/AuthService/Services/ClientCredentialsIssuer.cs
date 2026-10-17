using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthService.Models;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Services;

/// <summary>
/// OAuth2 client-credentials grant (RFC 6749 §4.4) for machine clients — the shared
/// authorization server of the demo. Tokens are signed with the same key/issuer as user
/// tokens; "aud" limits them to the client's group of services, "scope" to its permissions.
/// </summary>
public class ClientCredentialsIssuer
{
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, OAuthClient> _clients;

    public ClientCredentialsIssuer(IConfiguration configuration)
    {
        _configuration = configuration;
        _clients = (configuration.GetSection("OAuthClients").Get<List<OAuthClient>>() ?? new())
            .Where(c => !string.IsNullOrEmpty(c.ClientId))
            .ToDictionary(c => c.ClientId, StringComparer.Ordinal);
    }

    /// <summary>Returns the token response, or the RFC 6749 error with its HTTP status.</summary>
    public (TokenResponse? Token, OAuthError? Error, int Status) Issue(IFormCollection form, string? authorizationHeader)
    {
        if (form["grant_type"] != "client_credentials")
            return Fail(400, "unsupported_grant_type", "Only grant_type=client_credentials is supported");

        var (clientId, secret) = ReadClientCredentials(form, authorizationHeader);
        if (string.IsNullOrEmpty(clientId) || !_clients.TryGetValue(clientId, out var client)
            || string.IsNullOrEmpty(client.Secret) || !FixedTimeEquals(client.Secret, secret))
            return Fail(401, "invalid_client", "Unknown client or wrong secret");

        var requested = form["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var notAllowed = requested.Except(client.Scopes).ToList();
        if (notAllowed.Count > 0)
            return Fail(400, "invalid_scope", $"Scope not allowed for this client: {string.Join(' ', notAllowed)}");
        var scopes = requested.Length > 0 ? requested.Distinct().ToList() : client.Scopes;

        var audience = form["audience"].ToString();
        if (audience.Length > 0 && !client.Audiences.Contains(audience))
            return Fail(400, "invalid_target", $"Audience not allowed for this client: {audience}");
        var audiences = audience.Length > 0 ? new List<string> { audience } : client.Audiences;

        var lifetime = TimeSpan.FromMinutes(_configuration.GetValue("Jwt:ExpirationMinutes", 60));
        return (new TokenResponse
        {
            AccessToken = CreateJwt(client.ClientId, scopes, audiences, lifetime),
            ExpiresIn = (int)lifetime.TotalSeconds,
            Scope = string.Join(' ', scopes),
        }, null, 200);
    }

    private string CreateJwt(string clientId, IReadOnlyList<string> scopes, IReadOnlyList<string> audiences, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:SecretKey"]!));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new("client_id", clientId),
            new("scope", string.Join(' ', scopes)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(audiences.Select(a => new Claim(JwtRegisteredClaimNames.Aud, a)));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>client_secret_basic (Authorization: Basic) or client_secret_post (form fields).</summary>
    private static (string? ClientId, string? Secret) ReadClientCredentials(IFormCollection form, string? authorizationHeader)
    {
        if (authorizationHeader?.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorizationHeader["Basic ".Length..].Trim()));
                var separator = decoded.IndexOf(':');
                if (separator > 0)
                    return (Uri.UnescapeDataString(decoded[..separator]), Uri.UnescapeDataString(decoded[(separator + 1)..]));
            }
            catch (FormatException)
            {
                return (null, null);
            }
        }
        return (form["client_id"].ToString(), form["client_secret"].ToString());
    }

    private static bool FixedTimeEquals(string expected, string? actual) =>
        actual is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private static (TokenResponse?, OAuthError?, int) Fail(int status, string error, string description) =>
        (null, new OAuthError { Error = error, ErrorDescription = description }, status);
}
