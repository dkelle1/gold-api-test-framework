using System.Net;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.ProductService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
using ApiTestFramework.Steps.DataGenerators;
using RestSharp;
using Serilog;

namespace ApiTestFramework.Steps.ServiceSteps;

/// <summary>
/// Step class for ProductService operations.
/// Used to prepare test data (e.g., creating products needed by OrderService tests).
/// </summary>
public class ProductServiceSteps
{
    private readonly ApiClient _client;
    private readonly ILogger _logger;

    public ProductServiceSteps()
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");
        _logger = Log.ForContext<ProductServiceSteps>();
    }

    /// <summary>
    /// Creates a product with random data and returns the created product.
    /// </summary>
    [AllureStep("Create a product with random data")]
    public async Task<Product> CreateProductAsync()
    {
        var request = ProductDataGenerator.GenerateCreateProductRequest();
        return await CreateProductAsync(request);
    }

    /// <summary>
    /// Creates a product from a specific request and returns the created product.
    /// </summary>
    [AllureStep("Create a product")]
    public async Task<Product> CreateProductAsync(CreateProductRequest createRequest)
    {
        _logger.Information("Creating product: {Name}", createRequest.Name);

        var response = await _client.SendAsync<Product>(
            RequestFactory.Post(ProductServiceRoutes.Base, createRequest));

        response.ShouldHaveStatusCode(HttpStatusCode.Created);
        var product = response.ShouldHaveData();

        _logger.Information("Created product with Id: {Id}", product.Id);
        return product;
    }

    /// <summary>
    /// Gets a product by Id.
    /// </summary>
    [AllureStep("Get product by Id: {id}")]
    public async Task<RestResponse<Product>> GetProductAsync(int id)
    {
        var response = await _client.SendAsync<Product>(
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", id));

        return response;
    }

    /// <summary>
    /// Gets all products.
    /// </summary>
    [AllureStep("Get all products")]
    public async Task<RestResponse<List<Product>>> GetAllProductsAsync()
    {
        var response = await _client.SendAsync<List<Product>>(
            RequestFactory.Get(ProductServiceRoutes.Base));

        return response;
    }

    /// <summary>
    /// Updates a product.
    /// </summary>
    [AllureStep("Update product with Id: {id}")]
    public async Task<RestResponse<Product>> UpdateProductAsync(int id, UpdateProductRequest updateRequest)
    {
        var response = await _client.SendAsync<Product>(
            RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", id)
                .WithBody(updateRequest));

        return response;
    }

    /// <summary>
    /// Deletes a product.
    /// </summary>
    [AllureStep("Delete product with Id: {id}")]
    public async Task<RestResponse> DeleteProductAsync(int id)
    {
        var response = await _client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", id));

        return response;
    }

    // ── Negative-path helpers (raw RestResponse for status-code assertions) ─────

    /// <summary>
    /// Attempts to create a product and returns the raw response.
    /// Use for negative test scenarios (validation errors, auth failures).
    /// </summary>
    [AllureStep("Attempt to create a product (raw response)")]
    public async Task<RestResponse<Product>> TryCreateProductAsync(CreateProductRequest createRequest)
        => await _client.SendAsync<Product>(RequestFactory.Post(ProductServiceRoutes.Base, createRequest));

    /// <summary>
    /// Attempts to update a product and returns the raw response.
    /// </summary>
    [AllureStep("Attempt to update product with Id: {id} (raw response)")]
    public async Task<RestResponse<Product>> TryUpdateProductAsync(int id, UpdateProductRequest updateRequest)
        => await _client.SendAsync<Product>(
            RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", id)
                .WithBody(updateRequest));

    /// <summary>
    /// Attempts to delete a product and returns the raw response.
    /// </summary>
    [AllureStep("Attempt to delete product with Id: {id} (raw response)")]
    public async Task<RestResponse> TryDeleteProductAsync(int id)
        => await _client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", id));
}
