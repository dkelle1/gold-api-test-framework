using ApiTestFramework.Clients.AuthService;
using ApiTestFramework.Steps.Builders.AuthService;

namespace ApiTestFramework.Steps.DataGenerators;

/// <summary>
/// Facade over the generated AuthService builders.
/// </summary>
public static class AuthDataGenerator
{
    public static RegisterRequest GenerateRegisterRequest()
        => new RegisterRequestBuilder().Build();

    public static LoginRequest GenerateLoginRequest(string username, string password)
        => new LoginRequestBuilder()
            .WithUsername(username)
            .WithPassword(password)
            .Build();
}
