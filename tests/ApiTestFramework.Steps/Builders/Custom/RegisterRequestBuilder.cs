using ApiTestFramework.Clients.AuthService;

namespace ApiTestFramework.Steps.Builders.AuthService;

// ============================================================================
// Hand-written half of the generated RegisterRequestBuilder (partial).
// Enforces invariants the OpenAPI spec cannot express; survives regeneration.
// ============================================================================
public partial class RegisterRequestBuilder
{
    /// <summary>
    /// AuthService password policy: min 8 chars, upper, lower, digit.
    /// Guarantees the built request always satisfies it, regardless of what
    /// heuristic the generator picked for the password field.
    /// </summary>
    private bool _usernameOverridden;

    partial void OnBeforeBuild(RegisterRequest instance)
    {
        if (string.IsNullOrWhiteSpace(instance.Password) || instance.Password.Length < 8)
            instance.Password = "TestPass123!";

        if (string.IsNullOrWhiteSpace(instance.Role))
            instance.Role = "User";

        // Generated default (name + 4 random digits) can collide across a big
        // parallel suite — append a GUID fragment unless the test set its own.
        if (!_usernameOverridden)
        {
            var baseName = instance.Username.Length > 21 ? instance.Username[..21] : instance.Username;
            instance.Username = $"{baseName}_{Guid.NewGuid().ToString("N")[..8]}";
        }
    }

    /// <summary>Sets an exact username, opting out of the uniqueness suffix.</summary>
    public RegisterRequestBuilder WithExactUsername(string username)
    {
        _usernameOverridden = true;
        return WithUsername(username);
    }
}
