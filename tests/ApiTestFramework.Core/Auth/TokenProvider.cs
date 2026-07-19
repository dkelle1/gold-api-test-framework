using System.Collections.Concurrent;

namespace ApiTestFramework.Core.Auth;

/// <summary>
/// Thread-safe store for Bearer tokens keyed by identity name.
/// The default identity is acquired once during test suite setup and shared by all ApiClients.
/// Additional named identities (e.g. "admin", "viewer") can be registered so that
/// individual requests can be sent with different permissions within the same suite.
/// </summary>
public static class TokenProvider
{
    /// <summary>
    /// Name of the identity used when no identity is specified explicitly.
    /// </summary>
    public const string DefaultIdentity = "default";

    private static readonly ConcurrentDictionary<string, string> _tokens =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the Bearer token of the default identity.
    /// </summary>
    public static string? Token => GetToken(DefaultIdentity);

    /// <summary>
    /// Sets the Bearer token of the default identity (typically called once during GlobalSetup).
    /// </summary>
    public static void SetToken(string token) => SetToken(DefaultIdentity, token);

    /// <summary>
    /// Registers (or replaces) the Bearer token of a named identity.
    /// </summary>
    public static void SetToken(string identity, string token) => _tokens[identity] = token;

    /// <summary>
    /// Gets the Bearer token of a named identity, or null if none is registered.
    /// </summary>
    public static string? GetToken(string identity) =>
        _tokens.TryGetValue(identity, out var token) ? token : null;

    /// <summary>
    /// Gets the Bearer token of a named identity, throwing if it was never registered.
    /// </summary>
    public static string GetRequiredToken(string identity) =>
        GetToken(identity) ?? throw new InvalidOperationException(
            $"No Bearer token registered for identity '{identity}'. " +
            $"Registered identities: [{string.Join(", ", RegisteredIdentities)}]. " +
            "Register it via TokenProvider.SetToken(identity, token), " +
            "AuthServiceSteps.RegisterIdentityAsync(...) or the Identities section of appsettings.test.json.");

    /// <summary>
    /// Names of all identities that currently have a token.
    /// </summary>
    public static IReadOnlyCollection<string> RegisteredIdentities => _tokens.Keys.ToArray();

    /// <summary>
    /// Clears all stored tokens (called during teardown).
    /// </summary>
    public static void Clear() => _tokens.Clear();

    /// <summary>
    /// Returns true if the default identity has a token.
    /// </summary>
    public static bool HasToken => HasTokenFor(DefaultIdentity);

    /// <summary>
    /// Returns true if the named identity has a token.
    /// </summary>
    public static bool HasTokenFor(string identity) => !string.IsNullOrEmpty(GetToken(identity));
}
