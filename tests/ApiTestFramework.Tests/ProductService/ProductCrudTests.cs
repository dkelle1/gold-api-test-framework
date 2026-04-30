using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.ProductService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Steps.DataGenerators;
using ApiTestFramework.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Tests.ProductService;

[TestFixture]
[AllureSuite("ProductService")]
[AllureFeature("Product CRUD Operations")]
public class ProductCrudTests : BaseTest
{
    private ProductServiceSteps _steps = null!;

    protected override void OnFixtureSetUp()
    {
        _steps = new ProductServiceSteps();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a new product can be created successfully")]
    public async Task CreateProduct_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = ProductDataGenerator.GenerateCreateProductRequest();

        // Act
        var product = await _steps.CreateProductAsync(request);

        // Assert — validate all fields from the request are in the response
        product.Id.Should().BeGreaterThan(0);
        product.Name.Should().Be(request.Name);
        product.Description.Should().Be(request.Description);
        product.Price.Amount.Should().Be(request.Price);
        product.Inventory.StockQuantity.Should().Be(request.StockQuantity);
        product.Category.Should().Be(request.Category);
        product.IsActive.Should().BeTrue();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a product can be retrieved by Id")]
    public async Task GetProduct_WithValidId_ReturnsProduct()
    {
        // Arrange — create a product first
        var created = await _steps.CreateProductAsync();

        // Act
        var response = await _steps.GetProductAsync(created.Id);

        // Assert — full DTO comparison (excluding UpdatedAt which is set server-side on update)
        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        response.ShouldMatchDto(created, "GET Product — full DTO comparison");
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that requesting a non-existing product returns 404")]
    public async Task GetProduct_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _steps.GetProductAsync(NonExistentId);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that all products can be listed")]
    public async Task GetAllProducts_ReturnsProductList()
    {
        // Arrange — create a couple of products
        await _steps.CreateProductAsync();
        await _steps.CreateProductAsync();

        // Act
        var response = await _steps.GetAllProductsAsync();

        // Assert
        response.ShouldBeSuccessful();
        var products = response.ShouldHaveData();
        products.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a product can be updated")]
    public async Task UpdateProduct_WithValidData_ReturnsUpdated()
    {
        // Arrange
        var created = await _steps.CreateProductAsync();
        var updateRequest = ProductDataGenerator.GenerateUpdateProductRequest();

        // Act
        var response = await _steps.UpdateProductAsync(created.Id, updateRequest);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var updated = response.ShouldHaveData();
        updated.Name.Should().Be(updateRequest.Name);
        updated.Price.Amount.Should().Be(updateRequest.Price!.Value);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a product can be deleted")]
    public async Task DeleteProduct_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var created = await _steps.CreateProductAsync();

        // Act
        var deleteResponse = await _steps.DeleteProductAsync(created.Id);

        // Assert
        deleteResponse.ShouldHaveStatusCode(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await _steps.GetProductAsync(created.Id);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that deleting a non-existing product returns 404")]
    public async Task DeleteProduct_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _steps.DeleteProductAsync(NonExistentId);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }
}
