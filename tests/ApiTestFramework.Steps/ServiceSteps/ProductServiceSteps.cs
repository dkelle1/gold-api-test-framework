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
/// By default requests use the global token; bind a different identity per instance
/// via AsIdentity(name) or WithToken(token).
/// </summary>
public class ProductServiceSteps
{
    private readonly ApiClient _client;
    private readonly ILogger _logger;
    private readonly string? _identity;
    private readonly string? _bearerToken;

    public ProductServiceSteps() : this(identity: null, bearerToken: null)
    {
    }

    private ProductServiceSteps(string? identity, string? bearerToken)
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");
        _logger = Log.ForContext<ProductServiceSteps>();
        _identity = identity;
        _bearerToken = bearerToken;
    }

    /// <summary>
    /// Returns a copy of these steps that sends every request as the given
    /// named identity registered in TokenProvider (e.g. "admin").
    /// </summary>
    public ProductServiceSteps AsIdentity(string identity) => new(identity, bearerToken: null);

    /// <summary>
    /// Returns a copy of these steps that sends every request with the given Bearer token.
    /// </summary>
    public ProductServiceSteps WithToken(string bearerToken) => new(identity: null, bearerToken: bearerToken);

    /// <summary>
    /// Applies the bound identity/token (if any) to a request builder.
    /// Without a binding the request falls back to the default auto-injected token.
    /// </summary>
    private RequestBuilder Authorized(RequestBuilder builder) =>
        _bearerToken != null ? builder.WithBearerToken(_bearerToken)
        : _identity != null ? builder.AsIdentity(_identity)
        : builder;

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
            Authorized(RequestFactory.Post(ProductServiceRoutes.Base, createRequest)));

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
            Authorized(RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", id)));

        return response;
    }

    /// <summary>
    /// Gets all products.
    /// </summary>
    [AllureStep("Get all products")]
    public async Task<RestResponse<List<Product>>> GetAllProductsAsync()
    {
        var response = await _client.SendAsync<List<Product>>(
            Authorized(RequestFactory.Get(ProductServiceRoutes.Base)));

        return response;
    }

    /// <summary>
    /// Updates a product.
    /// </summary>
    [AllureStep("Update product with Id: {id}")]
    public async Task<RestResponse<Product>> UpdateProductAsync(int id, UpdateProductRequest updateRequest)
    {
        var response = await _client.SendAsync<Product>(
            Authorized(RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", id)
                .WithBody(updateRequest)));

        return response;
    }

    /// <summary>
    /// Deletes a product.
    /// </summary>
    [AllureStep("Delete product with Id: {id}")]
    public async Task<RestResponse> DeleteProductAsync(int id)
    {
        var response = await _client.SendAsync(
            Authorized(RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(ProductServiceRoutes.ById)
                .WithPathSegment("id", id)));

        return response;
    }

    /// <summary>
    /// Creates a product and returns only the Id — useful for setup steps.
    /// </summary>
    [AllureStep("Create product and return Id")]
    public async Task<int> CreateProductAndReturnIdAsync()
    {
        var product = await CreateProductAsync();
        return product.Id;
    }
}
