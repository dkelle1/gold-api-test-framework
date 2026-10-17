using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.DI;
using DemoShop.Clients;
using DemoShop.Clients.ProductService;
using DemoShop.Steps.DataGenerators;
using DemoShop.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace DemoShop.Tests.ProductService;

/// <summary>
/// GET /api/products/category/{category} — promoted from the generated scaffold
/// (DemoShop.Tests/Generated/ProductServiceGeneratedTests.cs.txt).
/// Worked example of docs/tutorial/05-scaffold-and-promote-tests.md.
/// </summary>
[TestFixture]
[AllureSuite("ProductService")]
[AllureFeature("Browse by category")]
public class ProductCategoryTests : BaseTest
{
    private ProductServiceSteps _products = null!;
    private ProductServiceSteps _admin = null!;

    protected override void OnFixtureSetUp()
    {
        _products = new ProductServiceSteps();
        _admin = _products.AsIdentity(TestIdentities.Admin); // DELETE is admin-only
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Only active products of exactly the requested category are returned")]
    public async Task GetProductsByCategory_ReturnsOnlyActiveProductsOfThatCategory()
    {
        // Arrange — a category no other test uses keeps the assertion exact
        var category = $"cat-{Guid.NewGuid():N}"[..20];
        var active = await CreateProductAsync(category);
        var deactivated = await CreateProductAsync(category);
        var otherCategory = await CreateProductAsync($"other-{Guid.NewGuid():N}"[..20]);

        var deactivate = await _products.UpdateProductAsync(deactivated.Id, new UpdateProductRequest { IsActive = false });
        deactivate.ShouldHaveStatusCode(HttpStatusCode.OK);

        // Act
        var response = await _products.GetProductsByCategoryAsync(category);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var products = response.ShouldHaveData();
        products.Select(p => p.Id).Should().Equal(active.Id);
        products.Should().OnlyContain(p => p.Category == category && p.IsActive);
        products.Select(p => p.Id).Should().NotContain(new[] { deactivated.Id, otherCategory.Id });
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("An unknown category is not an error — the list is just empty")]
    public async Task GetProductsByCategory_WithUnknownCategory_ReturnsEmptyList()
    {
        var response = await _products.GetProductsByCategoryAsync($"none-{Guid.NewGuid():N}");

        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        response.ShouldHaveData().Should().BeEmpty();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("The endpoint requires a Bearer token")]
    public async Task GetProductsByCategory_WithoutToken_ReturnsUnauthorized()
    {
        var client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");

        var response = await client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(ProductServiceRoutes.ByCategory)
                .WithPathSegment("category", "anything")
                .WithoutAuth());

        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    private async Task<Product> CreateProductAsync(string category)
    {
        var product = await _products.CreateProductAsync(
            ProductDataGenerator.GenerateCreateProductRequest(r => r.Category = category));
        RegisterCleanup(() => _admin.DeleteProductAsync(product.Id));
        return product;
    }
}
