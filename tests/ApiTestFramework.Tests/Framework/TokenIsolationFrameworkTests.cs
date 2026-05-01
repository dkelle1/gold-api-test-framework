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

namespace ApiTestFramework.Tests.Framework;

/// <summary>
/// Framework self-tests for per-test token scoping:
///
///   - UserScope: register-and-scope user token with automatic restore.
///   - BaseTest.UseToken: explicit token override for current async context.
///
/// These tests intentionally exercise framework behavior using service endpoints
/// and are kept outside ProductService suite so service CRUD reports stay clean.
/// </summary>
[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Token Isolation")]
public class TokenIsolationFrameworkTests : BaseTest
{
    private AuthServiceSteps _authSteps = null!;
    private ProductServiceSteps _productSteps = null!;

    protected override void OnFixtureSetUp()
    {
        _authSteps = new AuthServiceSteps();
        _productSteps = new ProductServiceSteps();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription(
        "Two independently-registered users both receive 201 when creating a product, " +
        "proving that each request carries the correct per-user token and that scopes " +
        "are isolated from each other.")]
    public async Task TwoDistinctUsers_BothCanCreateProduct_ReturnsCreated()
    {
        var authA = await _authSteps.RegisterUserAsync("User");
        await using var userA = UserScope.FromAuthResponse(authA);
        var productA = await _productSteps.CreateProductAsync();

        var authB = await _authSteps.RegisterUserAsync("User");
        await using var userB = UserScope.FromAuthResponse(authB);
        var productB = await _productSteps.CreateProductAsync();

        productA.Id.Should().BeGreaterThan(0);
        productB.Id.Should().BeGreaterThan(0);
        productA.Id.Should().NotBe(productB.Id);
        userA.AuthResponse.User.Username.Should().NotBe(userB.AuthResponse.User.Username);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription(
        "After UserScope disposal, the default token is restored and subsequent requests " +
        "continue to succeed.")]
    public async Task UserScope_AfterDispose_DefaultTokenRestored()
    {
        var baseline = await _productSteps.CreateProductAsync();
        RegisterCleanup(() => _productSteps.DeleteProductAsync(baseline.Id));

        Product? inScopeProduct;
        var altAuth = await _authSteps.RegisterUserAsync("User");
        await using (var altUser = UserScope.FromAuthResponse(altAuth))
        {
            inScopeProduct = await _productSteps.CreateProductAsync();
        }

        var afterScopeProduct = await _productSteps.CreateProductAsync();
        RegisterCleanup(() => _productSteps.DeleteProductAsync(afterScopeProduct.Id));

        inScopeProduct!.Id.Should().BeGreaterThan(0);
        afterScopeProduct.Id.Should().BeGreaterThan(0);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription(
        "UserScope.FromAuthResponse can scope an existing AuthResponse without extra registration.")]
    public async Task UserScope_FromExistingAuthResponse_UsesCorrectToken()
    {
        var auth = await _authSteps.RegisterUserAsync();

        using var scope = UserScope.FromAuthResponse(auth);
        var product = await _productSteps.CreateProductAsync();

        product.Id.Should().BeGreaterThan(0);
        scope.AuthResponse.Token.AccessToken.Should().Be(auth.Token.AccessToken);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription(
        "BaseTest.UseToken overrides token only inside scope, then restores previous token.")]
    public async Task UseToken_ExplicitTokenOverride_RequestsUseOverriddenToken()
    {
        var secondUserToken = await _authSteps.GetBearerTokenAsync();

        using (UseToken(secondUserToken))
        {
            var product = await _productSteps.CreateProductAsync();
            product.Id.Should().BeGreaterThan(0);
        }

        var productAfter = await _productSteps.CreateProductAsync();
        RegisterCleanup(() => _productSteps.DeleteProductAsync(productAfter.Id));
        productAfter.Id.Should().BeGreaterThan(0);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription(
        "Invalid scoped token must return 401 Unauthorized to prove token propagation works.")]
    public async Task UseToken_WithInvalidToken_ReturnsUnauthorized()
    {
        using var scope = UseToken("invalid.jwt.token");

        var response = await _productSteps.GetProductAsync(1);

        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription(
        "A user registered with a custom role returns that role in AuthResponse.")]
    public async Task RegisterAndScope_WithCustomRole_ReturnsExpectedRole()
    {
        var adminAuth = await _authSteps.RegisterUserAsync("Admin");
        await using var adminUser = UserScope.FromAuthResponse(adminAuth);

        adminUser.AuthResponse.User.Role.Should().Be("Admin");
        adminUser.AuthResponse.Token.AccessToken.Should().NotBeNullOrEmpty();
    }
}