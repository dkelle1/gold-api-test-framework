using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.ProductService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Steps.Auth;
using ApiTestFramework.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Tests.ProductService;

/// <summary>
/// Tests that verify the per-test multi-user token mechanism:
///
///   • <see cref="UserScope"/>   — registers a fresh user, overrides the token for the
///                                 duration of the scope, then restores the previous token.
///   • <see cref="BaseTest.UseToken"/> — directly scopes an already-obtained token without
///                                 creating a new user.
///
/// The global token set in <see cref="GlobalSetup"/> is the "default" user shared by all
/// other test fixtures. These tests deliberately step outside that default to prove the
/// isolation mechanism works correctly.
/// </summary>
[TestFixture]
[AllureSuite("ProductService")]
[AllureFeature("Multi-User Token Isolation")]
public class MultiUserProductTests : BaseTest
{
    private AuthServiceSteps _authSteps = null!;
    private ProductServiceSteps _productSteps = null!;

    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _authSteps = new AuthServiceSteps();
        _productSteps = new ProductServiceSteps();
    }

    // ── Pattern 1: UserScope — register-and-scope ────────────────────────────

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription(
        "Two independently-registered users both receive 201 when creating a product, " +
        "proving that each request carries the correct per-user token and that the scopes " +
        "are isolated from each other.")]
    public async Task TwoDistinctUsers_BothCanCreateProduct_ReturnsCreated()
    {
        // Arrange — register two separate users, each in their own scope
        await using var userA = await _authSteps.CreateUserScopeAsync();
        var productA = await _productSteps.CreateProductAsync();

        await using var userB = await _authSteps.CreateUserScopeAsync();
        var productB = await _productSteps.CreateProductAsync();

        // Assert — both products were created under different tokens
        productA.Id.Should().BeGreaterThan(0);
        productB.Id.Should().BeGreaterThan(0);
        productA.Id.Should().NotBe(productB.Id);

        userA.AuthResponse.User.Username.Should().NotBe(userB.AuthResponse.User.Username);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription(
        "After a UserScope is disposed the default (global) token is restored automatically. " +
        "A product creation after the scope must still succeed using the global token.")]
    public async Task UserScope_AfterDispose_DefaultTokenRestored()
    {
        // Arrange — create a product using the default global token (baseline)
        var baseline = await _productSteps.CreateProductAsync();
        RegisterCleanup(() => _productSteps.DeleteProductAsync(baseline.Id));

        // Act — switch to a different user inside the scope
        Product? inScopeProduct;
        await using (var altUser = await _authSteps.CreateUserScopeAsync())
        {
            inScopeProduct = await _productSteps.CreateProductAsync();
        }
        // userScope disposed here — global token restored

        // Act — create another product; must use the global token again
        var afterScopeProduct = await _productSteps.CreateProductAsync();
        RegisterCleanup(() => _productSteps.DeleteProductAsync(afterScopeProduct.Id));

        // Assert
        inScopeProduct!.Id.Should().BeGreaterThan(0);
        afterScopeProduct.Id.Should().BeGreaterThan(0);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription(
        "UserScope.FromAuthResponse lets a test reuse an AuthResponse obtained earlier " +
        "(e.g., via a login step) without registering a second time.")]
    public async Task UserScope_FromExistingAuthResponse_UsesCorrectToken()
    {
        // Arrange — register a user manually and keep the response
        var auth = await _authSteps.RegisterUserAsync();

        // Act — enter a scope from the existing AuthResponse
        using var scope = UserScope.FromAuthResponse(auth);
        var product = await _productSteps.CreateProductAsync();

        // Assert
        product.Id.Should().BeGreaterThan(0);
        scope.AuthResponse.Token.AccessToken.Should().Be(auth.Token.AccessToken);
    }

    // ── Pattern 2: UseToken — raw token scope ────────────────────────────────

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription(
        "BaseTest.UseToken overrides the token for the current async context without " +
        "registering a new user. The override is in effect only inside the using block.")]
    public async Task UseToken_ExplicitTokenOverride_RequestsUseOverriddenToken()
    {
        // Arrange — get a token for a second user
        var secondUserToken = await _authSteps.GetBearerTokenAsync();

        // Act — temporarily override the token
        using (UseToken(secondUserToken))
        {
            var product = await _productSteps.CreateProductAsync();
            product.Id.Should().BeGreaterThan(0);
        }
        // UseToken scope disposed — global token restored

        // Act — after the scope the global token is active again
        var productAfter = await _productSteps.CreateProductAsync();
        RegisterCleanup(() => _productSteps.DeleteProductAsync(productAfter.Id));
        productAfter.Id.Should().BeGreaterThan(0);
    }

    // ── Pattern 3: Invalid token returns 401 ────────────────────────────────

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription(
        "A request sent with an invalid token must return 401 Unauthorized, " +
        "confirming that token isolation correctly propagates per-request credentials.")]
    public async Task UseToken_WithInvalidToken_ReturnsUnauthorized()
    {
        using var scope = UseToken("invalid.jwt.token");

        var response = await _productSteps.GetProductAsync(1);

        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    // ── Pattern 4: Custom role ───────────────────────────────────────────────

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription(
        "A user registered with a custom role receives that role in the AuthResponse, " +
        "verifying that role-based registration works end-to-end.")]
    public async Task CreateUserScopeAsync_WithCustomRole_ReturnsExpectedRole()
    {
        await using var adminUser = await _authSteps.CreateUserScopeAsync(role: "Admin");

        adminUser.AuthResponse.User.Role.Should().Be("Admin");
        adminUser.AuthResponse.Token.AccessToken.Should().NotBeNullOrEmpty();
    }
}
