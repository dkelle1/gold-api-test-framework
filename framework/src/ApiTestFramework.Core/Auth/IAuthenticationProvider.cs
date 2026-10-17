namespace ApiTestFramework.Core.Auth;

/// <summary>
/// A single authentication header to attach to outgoing requests,
/// e.g. ("Authorization", "Bearer eyJ...") or ("X-Api-Key", "secret").
/// </summary>
public sealed record AuthenticationHeader(string Name, string Value);

/// <summary>
/// Strategy for authenticating outgoing requests. The framework consults the
/// active provider (see <see cref="AuthenticationContext"/>) on every
/// <c>RequestBuilder.Build()</c> and attaches the returned header unless the
/// test has already set a header with the same name explicitly.
///
/// Built-in implementations cover the common schemes (session token, static
/// token, API key, OAuth2 client credentials). Products with custom schemes
/// implement this interface and plug in via configuration
/// (<c>Authentication:Mode = "Custom"</c>) or by assigning
/// <see cref="AuthenticationContext.Provider"/> in their SetUpFixture.
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// Returns the header to attach, or null when no authentication should be
    /// added (anonymous APIs, or no credentials available yet).
    /// Implementations may perform I/O (e.g. fetch an OAuth2 token) but must
    /// cache aggressively — this is called for every request.
    /// </summary>
    AuthenticationHeader? GetAuthenticationHeader();
}
