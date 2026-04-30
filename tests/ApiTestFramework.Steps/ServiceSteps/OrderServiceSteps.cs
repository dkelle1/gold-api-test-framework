using System.Net;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.OrderService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Steps.DataGenerators;
using RestSharp;

namespace ApiTestFramework.Steps.ServiceSteps;

/// <summary>
/// Step class for OrderService operations.
/// Inherits generic CRUD from <see cref="CrudServiceStepsBase{TEntity,TCreate,TUpdate}"/>.
/// Adds service-specific convenience methods and cross-service setup helpers.
/// </summary>
public class OrderServiceSteps
    : CrudServiceSteps<Order, CreateOrderRequest, UpdateOrderRequest>
{
    private readonly ProductServiceSteps _productSteps;

    public OrderServiceSteps()
        : base("OrderService", OrderServiceRoutes.Base, OrderServiceRoutes.ById)
    {
        _productSteps = new ProductServiceSteps();
    }

    // ── Cross-service setup ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates an order backed by a freshly created product (full setup for tests
    /// that need an existing order without caring about specific product data).
    /// </summary>
    [AllureStep("Create order with new product (full setup)")]
    public async Task<(Order Order, int ProductId)> CreateOrderWithProductAsync()
    {
        var product = await _productSteps.CreateProductAsync();
        var createRequest = OrderDataGenerator.GenerateCreateOrderRequest(product.Id);
        var order = await CreateAsync(createRequest);
        return (order, product.Id);
    }

    // ── Named convenience wrappers (keep existing call-sites unchanged) ──────────

    [AllureStep("Create an order")]
    public Task<Order> CreateOrderAsync(CreateOrderRequest request)
        => CreateAsync(request);

    [AllureStep("Get order by Id: {id}")]
    public Task<RestResponse<Order>> GetOrderAsync(int id)
        => GetAsync(id);

    [AllureStep("Get all orders")]
    public Task<RestResponse<List<Order>>> GetAllOrdersAsync()
        => GetAllAsync();

    [AllureStep("Update order with Id: {id}")]
    public Task<RestResponse<Order>> UpdateOrderAsync(int id, UpdateOrderRequest request)
        => UpdateAsync(id, request);

    [AllureStep("Delete order with Id: {id}")]
    public Task<RestResponse> DeleteOrderAsync(int id)
        => DeleteAsync(id);

    // ── Extra route: orders by customer email ────────────────────────────────────

    [AllureStep("Get orders by customer email: {email}")]
    public Task<RestResponse<List<Order>>> GetOrdersByCustomerEmailAsync(string email)
        => Client.SendAsync<List<Order>>(
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(OrderServiceRoutes.ByCustomerEmail)
                .WithPathSegment("email", email));

    // ── Negative-path helpers ────────────────────────────────────────────────────

    [AllureStep("Attempt to create an order (raw response)")]
    public Task<RestResponse<Order>> TryCreateOrderAsync(CreateOrderRequest request)
        => TryCreateAsync(request);

    [AllureStep("Attempt to update order with Id: {id} (raw response)")]
    public Task<RestResponse<Order>> TryUpdateOrderAsync(int id, UpdateOrderRequest request)
        => TryUpdateAsync(id, request);
}

