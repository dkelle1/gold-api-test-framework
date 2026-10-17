using System.Net;
using Allure.NUnit.Attributes;
using DemoShop.Clients.OrderService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Client;
using DemoShop.Clients;
using ApiTestFramework.Core.DI;
using DemoShop.Steps.DataGenerators;
using RestSharp;
using Serilog;

namespace DemoShop.Steps.ServiceSteps;

/// <summary>
/// Step class for OrderService operations.
/// Depends on ProductServiceSteps for creating required products.
/// By default requests use the global token; bind a different identity per instance
/// via AsIdentity(name), WithToken(token) or AsClient(authClient) — the binding also applies to the
/// internal ProductServiceSteps used for setup (AsClient does not: products are
/// set up with the ProductService's own client).
/// </summary>
public class OrderServiceSteps
{
    private readonly ApiClient _client;
    private readonly ProductServiceSteps _productSteps;
    private readonly ILogger _logger;
    private readonly string? _identity;
    private readonly string? _bearerToken;
    private readonly string? _authClient;

    public OrderServiceSteps() : this(identity: null, bearerToken: null)
    {
    }

    private OrderServiceSteps(string? identity, string? bearerToken, string? authClient = null)
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("OrderService");
        _logger = Log.ForContext<OrderServiceSteps>();
        _identity = identity;
        _bearerToken = bearerToken;
        _authClient = authClient;

        var productSteps = new ProductServiceSteps();
        _productSteps = bearerToken != null ? productSteps.WithToken(bearerToken)
            : identity != null ? productSteps.AsIdentity(identity)
            : productSteps;
    }

    /// <summary>
    /// Returns a copy of these steps that sends every request as the given
    /// named identity registered in TokenProvider (e.g. "admin").
    /// </summary>
    public OrderServiceSteps AsIdentity(string identity) => new(identity, bearerToken: null);

    /// <summary>
    /// Returns a copy of these steps that sends every request with the given Bearer token.
    /// </summary>
    public OrderServiceSteps WithToken(string bearerToken) => new(identity: null, bearerToken: bearerToken);

    /// <summary>
    /// Returns a copy of these steps that authenticates with another named auth client
    /// (Authentication:Clients) instead of the service's own one, e.g. to verify that a
    /// token issued for another group of services is rejected.
    /// </summary>
    public OrderServiceSteps AsClient(string authClient) => new(identity: null, bearerToken: null, authClient);

    /// <summary>
    /// Applies the bound identity/token (if any) to a request builder.
    /// Without a binding the request falls back to the default auto-injected token.
    /// </summary>
    private RequestBuilder Authorized(RequestBuilder builder) =>
        _bearerToken != null ? builder.WithBearerToken(_bearerToken)
        : _identity != null ? builder.AsIdentity(_identity)
        : _authClient != null ? builder.AsClient(_authClient)
        : builder;

    /// <summary>
    /// Creates an order with a newly created product (full setup).
    /// This is the main "prepare" method for tests that need an existing order.
    /// </summary>
    [AllureStep("Create order with new product (full setup)")]
    public async Task<(Order Order, int ProductId)> CreateOrderWithProductAsync()
    {
        // Step 1: Create a product first (dependency)
        var productId = await _productSteps.CreateProductAndReturnIdAsync();

        // Step 2: Create order for that product
        var createRequest = OrderDataGenerator.GenerateCreateOrderRequest(productId);
        var order = await CreateOrderAsync(createRequest);

        return (order, productId);
    }

    /// <summary>
    /// Creates a multi-item order: creates <paramref name="productCount"/> products
    /// and one order with a line per product.
    /// </summary>
    [AllureStep("Create order with {productCount} products (full setup)")]
    public async Task<(Order Order, List<int> ProductIds)> CreateOrderWithProductsAsync(int productCount)
    {
        var productIds = new List<int>();
        for (var i = 0; i < productCount; i++)
            productIds.Add(await _productSteps.CreateProductAndReturnIdAsync());

        var createRequest = OrderDataGenerator.GenerateMultiItemOrderRequest(productIds.ToArray());
        var order = await CreateOrderAsync(createRequest);

        return (order, productIds);
    }

    /// <summary>
    /// Creates an order from a specific request.
    /// </summary>
    [AllureStep("Create an order")]
    public async Task<Order> CreateOrderAsync(CreateOrderRequest createRequest)
    {
        _logger.Information("Creating order with {ItemCount} item(s) for Customer: {Customer}",
            createRequest.Items.Count, createRequest.Customer.Name);

        var response = await _client.SendAsync<Order>(
            Authorized(RequestFactory.Post(OrderServiceRoutes.Base, createRequest)));

        response.ShouldHaveStatusCode(HttpStatusCode.Created);
        var order = response.ShouldHaveData();

        _logger.Information("Created order with Id: {Id}", order.Id);
        return order;
    }

    /// <summary>
    /// Attempts to create an order and returns the raw response.
    /// Use for negative scenarios (validation errors, unknown products, auth failures).
    /// </summary>
    [AllureStep("Attempt to create an order (raw response)")]
    public async Task<RestResponse<Order>> TryCreateOrderAsync(CreateOrderRequest createRequest)
        => await _client.SendAsync<Order>(
            Authorized(RequestFactory.Post(OrderServiceRoutes.Base, createRequest)));

    /// <summary>
    /// Gets an order by Id.
    /// </summary>
    [AllureStep("Get order by Id: {id}")]
    public async Task<RestResponse<Order>> GetOrderAsync(int id)
    {
        var response = await _client.SendAsync<Order>(
            Authorized(RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(OrderServiceRoutes.ById)
                .WithPathSegment("id", id)));

        return response;
    }

    /// <summary>
    /// Gets all orders.
    /// </summary>
    [AllureStep("Get all orders")]
    public async Task<RestResponse<List<Order>>> GetAllOrdersAsync()
    {
        var response = await _client.SendAsync<List<Order>>(
            Authorized(RequestFactory.Get(OrderServiceRoutes.Base)));

        return response;
    }

    /// <summary>
    /// Updates an order.
    /// </summary>
    [AllureStep("Update order with Id: {id}")]
    public async Task<RestResponse<Order>> UpdateOrderAsync(int id, UpdateOrderRequest updateRequest)
    {
        var response = await _client.SendAsync<Order>(
            Authorized(RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(OrderServiceRoutes.ById)
                .WithPathSegment("id", id)
                .WithBody(updateRequest)));

        return response;
    }

    /// <summary>
    /// Deletes an order.
    /// </summary>
    [AllureStep("Delete order with Id: {id}")]
    public async Task<RestResponse> DeleteOrderAsync(int id)
    {
        var response = await _client.SendAsync(
            Authorized(RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(OrderServiceRoutes.ById)
                .WithPathSegment("id", id)));

        return response;
    }

    /// <summary>
    /// Gets orders by customer email.
    /// </summary>
    [AllureStep("Get orders by customer email: {email}")]
    public async Task<RestResponse<List<Order>>> GetOrdersByCustomerEmailAsync(string email)
    {
        var response = await _client.SendAsync<List<Order>>(
            Authorized(RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(OrderServiceRoutes.ByCustomerEmail)
                .WithPathSegment("email", email)));

        return response;
    }
}
