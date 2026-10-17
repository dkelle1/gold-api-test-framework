using Microsoft.Extensions.Configuration;

namespace ApiTestFramework.Core.Auth;

/// <summary>
/// Holds the <see cref="IAuthenticationProvider"/>s consulted by
/// <c>RequestBuilder.Build()</c> for every request.
///
/// <see cref="Provider"/> is the default one. Named auth clients
/// (<c>Authentication:Clients:&lt;name&gt;</c>, selected per service with
/// <c>Services:&lt;key&gt;:AuthClient</c>) get their own provider from <see cref="For"/> -
/// e.g. one OAuth2 client per group of services behind a shared identity provider.
///
/// Resolution of each provider:
///   1. a provider assigned explicitly (<see cref="Provider"/> setter, <see cref="SetClient"/>),
///   2. otherwise built lazily from configuration,
///   3. with no configuration at all the default is
///      <see cref="SessionTokenAuthenticationProvider"/> — the framework's
///      original TokenProvider-based behaviour.
/// Providers are created once and shared by every service using them, so one
/// OAuth2 token is fetched and cached per client, not per service.
/// </summary>
public static class AuthenticationContext
{
    private static IAuthenticationProvider? _provider;
    private static readonly Dictionary<string, IAuthenticationProvider> _clients =
        new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// The provider of the named auth client, or <see cref="Provider"/> when
    /// <paramref name="client"/> is null or empty.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client is neither registered nor configured.</exception>
    public static IAuthenticationProvider For(string? client)
    {
        if (string.IsNullOrEmpty(client))
            return Provider;

        lock (_lock)
        {
            if (!_clients.TryGetValue(client, out var provider))
            {
                provider = AuthenticationProviderFactory.CreateForClient(
                    Configuration.ConfigurationProvider.Configuration, client);
                _clients[client] = provider;
            }
            return provider;
        }
    }

    /// <summary>Registers (or replaces) the provider of a named auth client.</summary>
    public static void SetClient(string client, IAuthenticationProvider provider)
    {
        ArgumentException.ThrowIfNullOrEmpty(client);
        ArgumentNullException.ThrowIfNull(provider);
        lock (_lock) { _clients[client] = provider; }
    }

    /// <summary>Reverts the default and all client providers to configuration-driven resolution.</summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _provider = null;
            _clients.Clear();
        }
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

    /// <summary>
    /// Builds the provider of the named auth client from <c>Authentication:Clients:&lt;client&gt;</c>.
    /// See <see cref="BindClientOptions"/> for how client settings combine with the shared ones.
    /// </summary>
    public static IAuthenticationProvider CreateForClient(IConfiguration configuration, string client)
        => Create(BindClientOptions(configuration, client));

    /// <summary>
    /// Options of a named auth client: the shared <c>Authentication</c> settings (Mode, HeaderName,
    /// Scheme, OAuth2:TokenUrl, Scope, Audience, ...) overridden by <c>Authentication:Clients:&lt;client&gt;</c>.
    /// Credentials (Token, ApiKey, OAuth2:ClientId, OAuth2:ClientSecret) are never inherited,
    /// so a client cannot silently authenticate with another client's secret.
    /// </summary>
    public static AuthenticationOptions BindClientOptions(IConfiguration configuration, string client)
    {
        var section = configuration.GetSection("Authentication");
        var clientSection = section.GetSection("Clients").GetSection(client);
        if (!clientSection.Exists())
            throw new InvalidOperationException(
                $"Authentication client '{client}' is not configured. Add Authentication:Clients:{client} " +
                $"(env: TEST_Authentication__Clients__{client}__...) or register a provider with " +
                $"AuthenticationContext.SetClient(\"{client}\", ...).");

        var options = new AuthenticationOptions();
        section.Bind(options);
        options.Token = string.Empty;
        options.ApiKey = string.Empty;
        options.OAuth2.ClientId = string.Empty;
        options.OAuth2.ClientSecret = string.Empty;
        clientSection.Bind(options);
        return options;
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
