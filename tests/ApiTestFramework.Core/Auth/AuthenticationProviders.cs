namespace ApiTestFramework.Core.Auth;

/// <summary>
/// Default provider — reads the token acquired during suite setup from the
/// static <see cref="TokenProvider"/>. Preserves the original framework
/// behaviour: a register/login flow stores the JWT once, every request
/// carries it. Returns null (no header) until a token has been stored.
/// </summary>
public sealed class SessionTokenAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _headerName;
    private readonly string _scheme;

    public SessionTokenAuthenticationProvider(string headerName = "Authorization",
                                              string scheme = "Bearer")
    {
        _headerName = headerName;
        _scheme = scheme;
    }

    public AuthenticationHeader? GetAuthenticationHeader()
        => TokenProvider.HasToken
            ? new AuthenticationHeader(_headerName, Schemes.Prefix(_scheme, TokenProvider.Token!))
            : null;
}

/// <summary>
/// Fixed token known up front (long-lived PAT, token issued by a vault or a
/// CI secret). Configure via Authentication:Token or TEST_Authentication__Token.
/// </summary>
public sealed class StaticTokenAuthenticationProvider : IAuthenticationProvider
{
    private readonly AuthenticationHeader? _header;

    public StaticTokenAuthenticationProvider(string token,
                                             string headerName = "Authorization",
                                             string scheme = "Bearer")
        => _header = string.IsNullOrEmpty(token)
            ? null
            : new AuthenticationHeader(headerName, Schemes.Prefix(scheme, token));

    public AuthenticationHeader? GetAuthenticationHeader() => _header;
}

/// <summary>
/// API-key authentication — the key is sent verbatim in a configurable header
/// (e.g. X-Api-Key). Configure via Authentication:ApiKey and
/// Authentication:HeaderName.
/// </summary>
public sealed class ApiKeyAuthenticationProvider : IAuthenticationProvider
{
    private readonly AuthenticationHeader? _header;

    public ApiKeyAuthenticationProvider(string apiKey, string headerName = "X-Api-Key")
        => _header = string.IsNullOrEmpty(apiKey)
            ? null
            : new AuthenticationHeader(headerName, apiKey);

    public AuthenticationHeader? GetAuthenticationHeader() => _header;
}

/// <summary>No authentication — for fully anonymous APIs.</summary>
public sealed class NoAuthenticationProvider : IAuthenticationProvider
{
    public AuthenticationHeader? GetAuthenticationHeader() => null;
}

/// <summary>
/// Adapter for fully custom flows without writing a class: wraps a delegate
/// that produces the current token (or null). Useful in a SetUpFixture:
/// <code>
/// AuthenticationContext.Provider =
///     new DelegateAuthenticationProvider(() => MyAuthSteps.CurrentToken);
/// </code>
/// </summary>
public sealed class DelegateAuthenticationProvider : IAuthenticationProvider
{
    private readonly Func<string?> _tokenFactory;
    private readonly string _headerName;
    private readonly string _scheme;

    public DelegateAuthenticationProvider(Func<string?> tokenFactory,
                                          string headerName = "Authorization",
                                          string scheme = "Bearer")
    {
        _tokenFactory = tokenFactory;
        _headerName = headerName;
        _scheme = scheme;
    }

    public AuthenticationHeader? GetAuthenticationHeader()
    {
        var token = _tokenFactory();
        return string.IsNullOrEmpty(token)
            ? null
            : new AuthenticationHeader(_headerName, Schemes.Prefix(_scheme, token));
    }
}

internal static class Schemes
{
    public static string Prefix(string scheme, string token)
        => string.IsNullOrEmpty(scheme) ? token : $"{scheme} {token}";
}
