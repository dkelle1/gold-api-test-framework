using ApiTestFramework.Clients.AuthService;
using Bogus;

namespace ApiTestFramework.Steps.DataGenerators;

/// <summary>
/// Bogus-based data generator for AuthService DTOs.
/// </summary>
public static class AuthDataGenerator
{
    private static readonly Faker _faker = new("en");

    public static RegisterRequest GenerateRegisterRequest()
    {
        return new RegisterRequest
        {
            Username = _faker.Internet.UserName() + _faker.Random.Number(1000, 9999),
            Email = _faker.Internet.Email(),
            Password = "TestPass123!",
            Role = "User"
        };
    }

    public static LoginRequest GenerateLoginRequest(string username, string password)
    {
        return new LoginRequest
        {
            Username = username,
            Password = password
        };
    }
}
