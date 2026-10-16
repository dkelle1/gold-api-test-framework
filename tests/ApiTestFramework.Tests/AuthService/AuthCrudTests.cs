using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.AuthService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
using ApiTestFramework.Steps.DataGenerators;
using ApiTestFramework.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Tests.AuthService;

[TestFixture]
[AllureSuite("AuthService")]
[AllureFeature("Authentication")]
public class AuthCrudTests : BaseTest
{
    private AuthServiceSteps _authSteps = null!;

    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _authSteps = new AuthServiceSteps();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a new user can be registered and receives a JWT token")]
    public async Task Register_WithValidData_ReturnsToken()
    {
        // Arrange
        var request = AuthDataGenerator.GenerateRegisterRequest();

        // Act
        var authResponse = await _authSteps.RegisterUserAsync(request);

        // Assert
        authResponse.Token.AccessToken.Should().NotBeNullOrEmpty();
        authResponse.User.Username.Should().Be(request.Username);
        authResponse.User.Email.Should().Be(request.Email);
        authResponse.User.Role.Should().Be(request.Role);
        authResponse.Token.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a registered user can login and receives a JWT token")]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        // Arrange — register a user first
        var registerRequest = AuthDataGenerator.GenerateRegisterRequest();
        await _authSteps.RegisterUserAsync(registerRequest);

        // Act
        var loginRequest = AuthDataGenerator.GenerateLoginRequest(
            registerRequest.Username, registerRequest.Password);
        var authResponse = await _authSteps.LoginAsync(loginRequest);

        // Assert
        authResponse.Token.AccessToken.Should().NotBeNullOrEmpty();
        authResponse.User.Username.Should().Be(registerRequest.Username);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that login with wrong password returns 401")]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        // Arrange — register a user first
        var registerRequest = AuthDataGenerator.GenerateRegisterRequest();
        await _authSteps.RegisterUserAsync(registerRequest);

        // Act
        var loginRequest = AuthDataGenerator.GenerateLoginRequest(
            registerRequest.Username, "WrongPassword123!");
        var response = await _authSteps.TryLoginAsync(loginRequest);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that login with non-existent user returns 401")]
    public async Task Login_WithNonExistentUser_ReturnsUnauthorized()
    {
        // Act
        var loginRequest = AuthDataGenerator.GenerateLoginRequest(
            "nonexistent_user_xyz", "SomePassword123!");
        var response = await _authSteps.TryLoginAsync(loginRequest);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that registering a duplicate username returns 409 Conflict")]
    public async Task Register_DuplicateUsername_ReturnsConflict()
    {
        // Arrange — register a user
        var registerRequest = AuthDataGenerator.GenerateRegisterRequest();
        await _authSteps.RegisterUserAsync(registerRequest);

        // Act — try to register with the same username
        var duplicateRequest = new RegisterRequest
        {
            Username = registerRequest.Username,
            Email = "different@email.com",
            Password = "TestPass123!",
            Role = "User"
        };
        var response = await _authSteps.TryRegisterUserAsync(duplicateRequest);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.Conflict);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that registration with empty username returns 400")]
    public async Task Register_WithEmptyUsername_ReturnsBadRequest()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Username = "",
            Email = "test@test.com",
            Password = "TestPass123!",
            Role = "User"
        };

        // Act
        var response = await _authSteps.TryRegisterUserAsync(request);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that accessing ProductService without token returns 401")]
    public async Task ProductService_WithoutToken_ReturnsUnauthorized()
    {
        // Act — call ProductService without Bearer token
        var client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");
        var response = await client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(RestSharp.Method.Get)
                .WithPath(ProductServiceRoutes.Base)
                .WithoutAuth());

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }
}
