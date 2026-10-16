using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.OrderService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Steps.Builders.OrderService;
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
        order.Customer.Name.Should().Be(request.Customer.Name);
        order.Customer.Email.Should().Be(request.Customer.Email);
        order.Items.Should().HaveCount(1);
        order.Items[0].ProductId.Should().Be(product.Id);
        order.Items[0].ProductName.Should().Be(product.Name);
        order.Items[0].Quantity.Should().Be(request.Items[0].Quantity);
        order.Items[0].UnitPrice.Should().Be(product.Price.Amount);
        order.TotalPrice.Should().Be(product.Price.Amount * request.Items[0].Quantity);
        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a single request with multiple nested item DTOs creates a multi-line order")]
    public async Task CreateOrder_WithMultipleItems_ReturnsAllLines()
    {
        // Arrange — two independent products, one order with two lines
        var productA = await _productSteps.CreateProductAsync();
        var productB = await _productSteps.CreateProductAsync();

        var request = new CreateOrderRequestBuilder()
            .ForProducts((productA.Id, 2), (productB.Id, 3))
            .Build();

        // Act
        var order = await _orderSteps.CreateOrderAsync(request);

        // Assert
        order.Items.Should().HaveCount(2);

        var lineA = order.Items.Single(i => i.ProductId == productA.Id);
        lineA.Quantity.Should().Be(2);
        lineA.UnitPrice.Should().Be(productA.Price.Amount);
        lineA.LineTotal.Should().Be(productA.Price.Amount * 2);

        var lineB = order.Items.Single(i => i.ProductId == productB.Id);
        lineB.Quantity.Should().Be(3);
        lineB.LineTotal.Should().Be(productB.Price.Amount * 3);

        order.TotalPrice.Should().Be(lineA.LineTotal + lineB.LineTotal);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that deeply nested DTOs (customer address, shipping) round-trip through the API")]
    public async Task CreateOrder_WithNestedAddressAndShipping_RoundTripsNestedDtos()
    {
        // Arrange — nested builders configure every DTO level explicitly
        var product = await _productSteps.CreateProductAsync();

        var request = new CreateOrderRequestBuilder()
            .WithCustomer(c => c
                .WithName("Jan Testowy")
                .WithEmail("jan.testowy@example.com")
                .WithAddress(a => a
                    .WithStreet("ul. Długa 1")
                    .WithCity("Gdańsk")
                    .WithPostalCode("80-001")
                    .WithCountry("Poland")))
            .WithShipping(s => s
                .WithMethod(ShippingMethod.Express)
                .WithNotes("Leave at the reception desk"))
            .ForProduct(product.Id, quantity: 1)
            .Build();

        // Act
        var order = await _orderSteps.CreateOrderAsync(request);

        // Assert — every nesting level made the round trip
        order.Customer.Name.Should().Be("Jan Testowy");
        order.Customer.Address.Should().NotBeNull();
        order.Customer.Address!.City.Should().Be("Gdańsk");
        order.Customer.Address.PostalCode.Should().Be("80-001");

        order.Shipping.Method.Should().Be(ShippingMethod.Express);
        order.Shipping.Notes.Should().Be("Leave at the reception desk");
        // Shipping address was omitted -> server falls back to the customer address
        order.Shipping.Address.Should().NotBeNull();
        order.Shipping.Address!.Street.Should().Be("ul. Długa 1");
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that creating an order with no items returns bad request")]
    public async Task CreateOrder_WithoutItems_ReturnsBadRequest()
    {
        // Arrange — builder default is an empty item list
        var request = new CreateOrderRequestBuilder().Build();

        // Act
        var apiClient = ApiTestFramework.Core.DI.ContainerProvider.ResolveNamed<ApiTestFramework.Core.Client.ApiClient>("OrderService");
        var response = await apiClient.SendAsync(
            ApiTestFramework.Core.Client.RequestFactory.Post("/api/orders", request));

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that creating an order with invalid product returns bad request")]
    public async Task CreateOrder_WithInvalidProduct_ReturnsBadRequest()
    {
        // Arrange
        var request = OrderDataGenerator.GenerateCreateOrderRequest(productId: 99999);

        // Act
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
    [AllureDescription("Verify that an order can be updated with a nested customer DTO")]
    public async Task UpdateOrder_WithValidData_ReturnsUpdated()
    {
        // Arrange
        var (order, _) = await _orderSteps.CreateOrderWithProductAsync();
        var updateRequest = new UpdateOrderRequestBuilder()
            .WithStatus(OrderStatus.Confirmed)
            .WithCustomer(c => c
                .WithName("Updated Customer Name")
                .WithAddress(a => a.WithCity("Warszawa")))
            .Build();

        // Act
        var response = await _orderSteps.UpdateOrderAsync(order.Id, updateRequest);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var updated = response.ShouldHaveData();
        updated.Customer.Name.Should().Be("Updated Customer Name");
        updated.Customer.Address.Should().NotBeNull();
        updated.Customer.Address!.City.Should().Be("Warszawa");
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
    [AllureDescription("Verify full order lifecycle: create (multi-item) -> update -> delete")]
    public async Task OrderLifecycle_FullFlow_Succeeds()
    {
        // Create a multi-item order (2 products) via the step helper
        var (order, productIds) = await _orderSteps.CreateOrderWithProductsAsync(productCount: 2);
        order.Status.Should().Be(OrderStatus.Pending);
        order.Items.Should().HaveCount(2);
        order.Items.Select(i => i.ProductId).Should().BeEquivalentTo(productIds);

        // Update to Confirmed
        var confirmResponse = await _orderSteps.UpdateOrderAsync(order.Id,
            new UpdateOrderRequestBuilder().WithStatus(OrderStatus.Confirmed).Build());
        confirmResponse.ShouldHaveStatusCode(HttpStatusCode.OK);
        confirmResponse.Data!.Status.Should().Be(OrderStatus.Confirmed);

        // Update to Shipped
        var shipResponse = await _orderSteps.UpdateOrderAsync(order.Id,
            new UpdateOrderRequestBuilder().WithStatus(OrderStatus.Shipped).Build());
        shipResponse.ShouldHaveStatusCode(HttpStatusCode.OK);
        shipResponse.Data!.Status.Should().Be(OrderStatus.Shipped);

        // Delete
        var deleteResponse = await _orderSteps.DeleteOrderAsync(order.Id);
        deleteResponse.ShouldHaveStatusCode(HttpStatusCode.NoContent);
    }
}
