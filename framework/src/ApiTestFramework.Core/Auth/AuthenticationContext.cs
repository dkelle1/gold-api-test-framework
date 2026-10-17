using Microsoft.Extensions.Configuration;

namespace ApiTestFramework.Core.Auth;

/// <summary>
/// Holds the active <see cref="IAuthenticationProvider"/> consulted by
/// <c>RequestBuilder.Build()</c> for every request.
///
/// Resolution order:
///   1. a provider assigned explicitly (e.g. in a SetUpFixture),
///   2. otherwise built lazily from the "Authentication" configuration section,
///   3. with no configuration at all this defaults to
///      <see cref="SessionTokenAuthenticationProvider"/> — the framework's
///      original TokenProvider-based behaviour.
/// </summary>
public static class AuthenticationContext
{
    private static IAuthenticationProvider? _provider;
    private static readonly object _lock = new();

    public static IAuthenticationProvider Provider
    {
        get
        {
            lock (_lock)
            {
                return _provider ??= AuthenticationProviderFactory.CreateFromConfiguration(
                    Configuration.ConfigurationProvider.Configuration);
            }
        }
        set
        {
            lock (_lock) { _provider = value; }
        }
    }

    /// <summary>Reverts to configuration-driven resolution on next access.</summary>
    public static void Reset()
    {
        lock (_lock) { _provider = null; }
    }
}

/// <summary>
/// Builds the <see cref="IAuthenticationProvider"/> selected by
/// <see cref="AuthenticationOptions.Mode"/>.
/// </summary>
public static class AuthenticationProviderFactory
{
    public static IAuthenticationProvider CreateFromConfiguration(IConfiguration configuration)
    {
        var options = new AuthenticationOptions();
        configuration.GetSection("Authentication").Bind(options);
        return Create(options);
    }

    public static IAuthenticationProvider Create(AuthenticationOptions options)
    {
        return options.Mode.ToLowerInvariant() switch
        {
            "sessiontoken" or "tokenprovider" or "jwt" =>
                new SessionTokenAuthenticationProvider(options.HeaderName, options.Scheme),

            "statictoken" =>
                new StaticTokenAuthenticationProvider(options.Token, options.HeaderName, options.Scheme),

            "apikey" =>
                new ApiKeyAuthenticationProvider(
                    options.ApiKey,
                    // "Authorization" is the generic default; an API key almost
                    // always lives in a dedicated header instead.
                    options.HeaderName == "Authorization" ? "X-Api-Key" : options.HeaderName),

            "oauth2clientcredentials" or "oauth2" =>
                new OAuth2ClientCredentialsProvider(options.OAuth2, options.HeaderName, options.Scheme),

            "none" =>
                new NoAuthenticationProvider(),

            "custom" =>
                CreateCustom(options.CustomProviderType),

            _ => throw new InvalidOperationException(
                $"Unknown Authentication:Mode '{options.Mode}'. Supported: " +
                "SessionToken, StaticToken, ApiKey, OAuth2ClientCredentials, None, Custom."),
        };
    }

    private static IAuthenticationProvider CreateCustom(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            throw new InvalidOperationException(
                "Authentication:Mode is 'Custom' but Authentication:CustomProviderType is empty. " +
                "Provide an assembly-qualified type name implementing IAuthenticationProvider.");

        var type = Type.GetType(typeName, throwOnError: true)!;
        if (Activator.CreateInstance(type) is not IAuthenticationProvider provider)
            throw new InvalidOperationException(
                $"{typeName} does not implement {nameof(IAuthenticationProvider)}.");
        return provider;
    }
}
