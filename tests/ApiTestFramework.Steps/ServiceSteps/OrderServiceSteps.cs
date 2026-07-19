using System.Net;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.OrderService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
using ApiTestFramework.Steps.DataGenerators;
using RestSharp;
using Serilog;

namespace ApiTestFramework.Steps.ServiceSteps;

/// <summary>
/// Step class for OrderService operations.
/// Depends on ProductServiceSteps for creating required products.
/// By default requests use the global token; bind a different identity per instance
/// via AsIdentity(name) or WithToken(token) — the binding also applies to the
/// internal ProductServiceSteps used for setup.
/// </summary>
public class OrderServiceSteps
{
    private readonly ApiClient _client;
    private readonly ProductServiceSteps _productSteps;
    private readonly ILogger _logger;
    private readonly string? _identity;
    private readonly string? _bearerToken;

    public OrderServiceSteps() : this(identity: null, bearerToken: null)
    {
    }

    private OrderServiceSteps(string? identity, string? bearerToken)
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("OrderService");
        _logger = Log.ForContext<OrderServiceSteps>();
        _identity = identity;
        _bearerToken = bearerToken;

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
    /// Applies the bound identity/token (if any) to a request builder.
    /// Without a binding the request falls back to the default auto-injected token.
    /// </summary>
    private RequestBuilder Authorized(RequestBuilder builder) =>
        _bearerToken != null ? builder.WithBearerToken(_bearerToken)
        : _identity != null ? builder.AsIdentity(_identity)
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
    /// Creates an order from a specific request.
    /// </summary>
    [AllureStep("Create an order")]
    public async Task<Order> CreateOrderAsync(CreateOrderRequest createRequest)
    {
        _logger.Information("Creating order for ProductId: {ProductId}, Customer: {Customer}",
            createRequest.ProductId, createRequest.CustomerName);

        var response = await _client.SendAsync<Order>(
            Authorized(RequestFactory.Post(OrderServiceRoutes.Base, createRequest)));

        response.ShouldHaveStatusCode(HttpStatusCode.Created);
        var order = response.ShouldHaveData();

        _logger.Information("Created order with Id: {Id}", order.Id);
        return order;
    }

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
