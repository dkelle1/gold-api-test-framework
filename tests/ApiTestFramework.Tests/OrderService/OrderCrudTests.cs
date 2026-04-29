using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.OrderService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Steps.DataGenerators;
using ApiTestFramework.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Tests.OrderService;

[TestFixture]
[AllureSuite("OrderService")]
[AllureFeature("Order CRUD Operations")]
public class OrderCrudTests : BaseTest
{
    private OrderServiceSteps _orderSteps = null!;
    private ProductServiceSteps _productSteps = null!;

    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _orderSteps = new OrderServiceSteps();
        _productSteps = new ProductServiceSteps();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that an order can be created with a valid product (cross-service dependency)")]
    public async Task CreateOrder_WithValidProduct_ReturnsCreated()
    {
        // Arrange — create a product in ProductService first
        var product = await _productSteps.CreateProductAsync();
        var request = OrderDataGenerator.GenerateCreateOrderRequest(product.Id);

        // Act
        var order = await _orderSteps.CreateOrderAsync(request);

        // Assert
        order.Id.Should().BeGreaterThan(0);
        order.Product.ProductId.Should().Be(product.Id);
        order.Product.ProductName.Should().Be(product.Name);
        order.Customer.Name.Should().Be(request.CustomerName);
        order.Product.Quantity.Should().Be(request.Quantity);
        order.Product.UnitPrice.Should().Be(product.Price.Amount);
        order.TotalPrice.Should().Be(product.Price.Amount * request.Quantity);
        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that creating an order with invalid product returns bad request")]
    public async Task CreateOrder_WithInvalidProduct_ReturnsBadRequest()
    {
        // Arrange
        var request = OrderDataGenerator.GenerateCreateOrderRequest(productId: 99999);

        // Act
        var response = await _orderSteps.GetOrderAsync(0); // This will just get a 404
        // Better: use the raw client to post with invalid product
        var apiClient = ApiTestFramework.Core.DI.ContainerProvider.ResolveNamed<ApiTestFramework.Core.Client.ApiClient>("OrderService");
        var rawResponse = await apiClient.SendAsync(
            ApiTestFramework.Core.Client.RequestFactory.Post("/api/orders", request));

        // Assert
        rawResponse.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that an order can be retrieved by Id")]
    public async Task GetOrder_WithValidId_ReturnsOrder()
    {
        // Arrange — full setup: create product + order
        var (order, _) = await _orderSteps.CreateOrderWithProductAsync();

        // Act
        var response = await _orderSteps.GetOrderAsync(order.Id);

        // Assert — full DTO comparison
        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        response.ShouldMatchDto(order, "GET Order — full DTO comparison");
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that requesting a non-existing order returns 404")]
    public async Task GetOrder_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _orderSteps.GetOrderAsync(99999);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that all orders can be listed")]
    public async Task GetAllOrders_ReturnsOrderList()
    {
        // Arrange
        await _orderSteps.CreateOrderWithProductAsync();

        // Act
        var response = await _orderSteps.GetAllOrdersAsync();

        // Assert
        response.ShouldBeSuccessful();
        var orders = response.ShouldHaveData();
        orders.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that an order can be updated")]
    public async Task UpdateOrder_WithValidData_ReturnsUpdated()
    {
        // Arrange
        var (order, _) = await _orderSteps.CreateOrderWithProductAsync();
        var updateRequest = new UpdateOrderRequest
        {
            CustomerName = "Updated Customer Name",
            Status = OrderStatus.Confirmed
        };

        // Act
        var response = await _orderSteps.UpdateOrderAsync(order.Id, updateRequest);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var updated = response.ShouldHaveData();
        updated.Customer.Name.Should().Be("Updated Customer Name");
        updated.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that an order can be deleted")]
    public async Task DeleteOrder_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var (order, _) = await _orderSteps.CreateOrderWithProductAsync();

        // Act
        var deleteResponse = await _orderSteps.DeleteOrderAsync(order.Id);

        // Assert
        deleteResponse.ShouldHaveStatusCode(HttpStatusCode.NoContent);

        // Verify it's gone
        var getResponse = await _orderSteps.GetOrderAsync(order.Id);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify full order lifecycle: create -> update -> delete")]
    public async Task OrderLifecycle_FullFlow_Succeeds()
    {
        // Create product
        var product = await _productSteps.CreateProductAsync();

        // Create order
        var createRequest = OrderDataGenerator.GenerateCreateOrderRequest(product.Id);
        var order = await _orderSteps.CreateOrderAsync(createRequest);
        order.Status.Should().Be(OrderStatus.Pending);

        // Update to Confirmed
        var confirmResponse = await _orderSteps.UpdateOrderAsync(order.Id, new UpdateOrderRequest
        {
            Status = OrderStatus.Confirmed
        });
        confirmResponse.ShouldHaveStatusCode(HttpStatusCode.OK);
        confirmResponse.Data!.Status.Should().Be(OrderStatus.Confirmed);

        // Update to Shipped
        var shipResponse = await _orderSteps.UpdateOrderAsync(order.Id, new UpdateOrderRequest
        {
            Status = OrderStatus.Shipped
        });
        shipResponse.ShouldHaveStatusCode(HttpStatusCode.OK);
        shipResponse.Data!.Status.Should().Be(OrderStatus.Shipped);

        // Delete
        var deleteResponse = await _orderSteps.DeleteOrderAsync(order.Id);
        deleteResponse.ShouldHaveStatusCode(HttpStatusCode.NoContent);
    }
}
