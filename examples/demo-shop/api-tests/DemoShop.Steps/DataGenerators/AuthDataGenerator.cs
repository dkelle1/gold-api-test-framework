using DemoShop.Clients.AuthService;
using DemoShop.Steps.Builders.AuthService;

namespace DemoShop.Steps.DataGenerators;

/// <summary>
/// Facade over the generated AuthService builders.
/// </summary>
public static class AuthDataGenerator
{
    public static RegisterRequest GenerateRegisterRequest(string role = "User")
        => new RegisterRequestBuilder().WithRole(role).Build();

    public static LoginRequest GenerateLoginRequest(string username, string password)
        => new LoginRequestBuilder()
            .WithUsername(username)
            .WithPassword(password)
            .Build();
}
