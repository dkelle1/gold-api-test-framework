using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
using ApiTestFramework.Steps.DataGenerators;
using ApiTestFramework.Steps.ServiceSteps;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Tests.ProductService;

/// <summary>
/// Verifies role-based authorization on ProductService using multiple identities
/// within a single suite: named identities from configuration ("admin", "user"),
/// ad-hoc tokens minted at test time, and anonymous requests.
/// </summary>
[TestFixture]
[AllureSuite("ProductService")]
[AllureFeature("Role-Based Authorization")]
public class ProductAuthorizationTests : BaseTest
{
    private ProductServiceSteps _steps = null!;
    private AuthServiceSteps _authSteps = null!;

    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _steps = new ProductServiceSteps();
        _authSteps = new AuthServiceSteps();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that consecutive requests can use different identities: a regular user gets 403 on delete, an admin succeeds")]
    public async Task DeleteProduct_UserThenAdmin_EnforcesRolePerRequest()
    {
        // Arrange — create a product with the default identity
        var product = await _steps.CreateProductAsync();

        // Act & Assert — same operation, two different tokens
        var asUser = await _steps.AsIdentity(TestIdentities.User).DeleteProductAsync(product.Id);
        asUser.ShouldHaveStatusCode(HttpStatusCode.Forbidden);

        // The product must survive the forbidden attempt
        var getResponse = await _steps.GetProductAsync(product.Id);
        getResponse.ShouldHaveStatusCode(HttpStatusCode.OK);

        var asAdmin = await _steps.AsIdentity(TestIdentities.Admin).DeleteProductAsync(product.Id);
        asAdmin.ShouldHaveStatusCode(HttpStatusCode.NoContent);

        // And be gone after the admin delete
        var getAfterDelete = await _steps.GetProductAsync(product.Id);
        getAfterDelete.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that deleting without a token returns 401 (authentication runs before role authorization)")]
    public async Task DeleteProduct_WithoutToken_ReturnsUnauthorized()
    {
        // Arrange
        var product = await _steps.CreateProductAsync();

        // Act — raw request with no Authorization header (auto-injection disabled)
        var client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");
        var response = await client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", product.Id)
                .WithoutAuth());

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that an admin token minted at test time can be bound to steps via WithToken")]
    public async Task DeleteProduct_WithAdHocAdminToken_ReturnsNoContent()
    {
        // Arrange — mint a brand-new admin at test time (not from configuration)
        var adHocAdmin = await _authSteps.RegisterUserAsync(
            AuthDataGenerator.GenerateRegisterRequest(role: "Admin"));
        var product = await _steps.CreateProductAsync();

        // Act — bind the raw token to the steps for this call only
        var response = await _steps.WithToken(adHocAdmin.Token.AccessToken)
            .DeleteProductAsync(product.Id);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.NoContent);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that a named identity can be used directly on the request builder")]
    public async Task GetProducts_AsAdminIdentityOnBuilder_ReturnsOk()
    {
        // Act — AsIdentity on the raw RequestBuilder resolves the token from TokenProvider
        var client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");
        var response = await client.SendAsync(
            RequestFactory.Get(ProductServiceRoutes.Base).AsIdentity(TestIdentities.Admin));

        // Assert
        response.ShouldBeSuccessful();
    }
}
