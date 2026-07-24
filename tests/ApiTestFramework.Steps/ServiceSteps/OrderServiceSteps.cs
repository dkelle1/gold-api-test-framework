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
/// </summary>
public class OrderServiceSteps
{
    private readonly ApiClient _client;
    private readonly ProductServiceSteps _productSteps;
    private readonly ILogger _logger;

    public OrderServiceSteps()
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("OrderService");
        _productSteps = new ProductServiceSteps();
        _logger = Log.ForContext<OrderServiceSteps>();
    }

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
            RequestFactory.Post(OrderServiceRoutes.Base, createRequest));

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
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(OrderServiceRoutes.ById)
                .WithPathSegment("id", id));

        return response;
    }

    /// <summary>
    /// Gets all orders.
    /// </summary>
    [AllureStep("Get all orders")]
    public async Task<RestResponse<List<Order>>> GetAllOrdersAsync()
    {
        var response = await _client.SendAsync<List<Order>>(
            RequestFactory.Get(OrderServiceRoutes.Base));

        return response;
    }

    /// <summary>
    /// Updates an order.
    /// </summary>
    [AllureStep("Update order with Id: {id}")]
    public async Task<RestResponse<Order>> UpdateOrderAsync(int id, UpdateOrderRequest updateRequest)
    {
        var response = await _client.SendAsync<Order>(
            RequestBuilder.Create()
                .WithMethod(Method.Put)
                .WithPath(OrderServiceRoutes.ById)
                .WithPathSegment("id", id)
                .WithBody(updateRequest));

        return response;
    }

    /// <summary>
    /// Deletes an order.
    /// </summary>
    [AllureStep("Delete order with Id: {id}")]
    public async Task<RestResponse> DeleteOrderAsync(int id)
    {
        var response = await _client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(Method.Delete)
                .WithPath(OrderServiceRoutes.ById)
                .WithPathSegment("id", id));

        return response;
    }

    /// <summary>
    /// Gets orders by customer email.
    /// </summary>
    [AllureStep("Get orders by customer email: {email}")]
    public async Task<RestResponse<List<Order>>> GetOrdersByCustomerEmailAsync(string email)
    {
        var response = await _client.SendAsync<List<Order>>(
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(OrderServiceRoutes.ByCustomerEmail)
                .WithPathSegment("email", email));

        return response;
    }
}
