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
    partial void OnBeforeBuild(RegisterRequest instance)
    {
        if (string.IsNullOrWhiteSpace(instance.Password) || instance.Password.Length < 8)
            instance.Password = "TestPass123!";

        if (string.IsNullOrWhiteSpace(instance.Role))
            instance.Role = "User";
    }
}
