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

    protected override void OnFixtureSetUp()
    {
        _orderSteps = new OrderServiceSteps();
        _productSteps = new ProductServiceSteps();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that an order can be created with a valid product (cross-service dependency)")]
    public async Task CreateOrder_WithValidProduct_ReturnsCreated()
    {
        // Arrange — create a product in ProductService first
        var productResponse = await _productSteps.CreateProductAsync();
        productResponse.ShouldHaveStatusCode(HttpStatusCode.Created);
        var product = productResponse.ShouldHaveData();
        var request = OrderDataGenerator.GenerateCreateOrderRequest(product.Id);

        // Act
        var orderResponse = await _orderSteps.CreateOrderAsync(request);

        // Assert
        orderResponse.ShouldHaveStatusCode(HttpStatusCode.Created);
        var order = orderResponse.ShouldHaveData();
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
        var request = OrderDataGenerator.GenerateCreateOrderRequest(productId: NonExistentId);

        // Act
        var response = await _orderSteps.TryCreateOrderAsync(request);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that an order can be retrieved by Id")]
    public async Task GetOrder_WithValidId_ReturnsOrder()
    {
        // Arrange — full setup: create product + order
        var (orderResponse, _) = await _orderSteps.CreateOrderWithProductAsync();
        orderResponse.ShouldHaveStatusCode(HttpStatusCode.Created);
        var order = orderResponse.ShouldHaveData();

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
        var response = await _orderSteps.GetOrderAsync(NonExistentId);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that all orders can be listed")]
    public async Task GetAllOrders_ReturnsOrderList()
    {
        // Arrange
        var (createResponse, _) = await _orderSteps.CreateOrderWithProductAsync();
        createResponse.ShouldHaveStatusCode(HttpStatusCode.Created);

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
        var (createResponse, _) = await _orderSteps.CreateOrderWithProductAsync();
        createResponse.ShouldHaveStatusCode(HttpStatusCode.Created);
        var order = createResponse.ShouldHaveData();
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
        var (createResponse, _) = await _orderSteps.CreateOrderWithProductAsync();
        createResponse.ShouldHaveStatusCode(HttpStatusCode.Created);
        var order = createResponse.ShouldHaveData();

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
        var productResponse = await _productSteps.CreateProductAsync();
        productResponse.ShouldHaveStatusCode(HttpStatusCode.Created);
        var product = productResponse.ShouldHaveData();

        // Create order
        var createRequest = OrderDataGenerator.GenerateCreateOrderRequest(product.Id);
        var orderResponse = await _orderSteps.CreateOrderAsync(createRequest);
        orderResponse.ShouldHaveStatusCode(HttpStatusCode.Created);
        var order = orderResponse.ShouldHaveData();
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
