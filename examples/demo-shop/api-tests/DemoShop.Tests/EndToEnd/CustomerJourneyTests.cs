using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using DemoShop.Clients.AuthService;
using DemoShop.Clients.OrderService;
using DemoShop.Clients.ProductService;
using DemoShop.Steps.Builders.OrderService;
using DemoShop.Steps.DataGenerators;
using DemoShop.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;

namespace DemoShop.Tests.EndToEnd;

/// <summary>
/// End-to-end business flows across all three services and several identities,
/// built only from the same steps, builders and identities the API tests use.
/// Worked example of docs/tutorial/07-end-to-end-flows.md.
/// Flows: examples/demo-shop/api-tests/flows/customer-journey.md (FLOW-SHOP-01, FLOW-SHOP-02).
/// </summary>
[TestFixture]
[AllureSuite("End-to-end")]
[AllureFeature("Customer journey")]
public class CustomerJourneyTests : BaseTest
{
    private AuthServiceSteps _auth = null!;
    private ProductServiceSteps _catalogAdmin = null!;
    private OrderServiceSteps _backOffice = null!;

    protected override void OnFixtureSetUp()
    {
        _auth = new AuthServiceSteps();
        // Back-office actors use the "admin" identity provisioned in GlobalSetup
        _catalogAdmin = new ProductServiceSteps().AsIdentity(TestIdentities.Admin);
        _backOffice = new OrderServiceSteps().AsIdentity(TestIdentities.Admin);
    }

    [Test]
    [AllureSeverity(SeverityLevel.blocker)]
    [AllureDescription("FLOW-SHOP-01: a new customer browses a category, orders two products, and follows the order until it ships")]
    public async Task NewCustomer_BrowsesOrdersAndReceivesShipment()
    {
        // 1. Catalog admin publishes two products in a fresh category
        var category = $"e2e-{Guid.NewGuid():N}"[..20];
        var keyboard = await PublishProductAsync(category, price: 49.99m);
        var mouse = await PublishProductAsync(category, price: 19.50m);

        // 2. A new customer signs up — an ad-hoc identity, registered at test time
        var customer = await RegisterCustomerAsync();
        var shop = new ProductServiceSteps().AsIdentity(customer.Identity);
        var myOrders = new OrderServiceSteps().AsIdentity(customer.Identity);

        // 3. The customer browses the category and sees exactly the published products
        var browse = await shop.GetProductsByCategoryAsync(category);
        browse.ShouldHaveStatusCode(HttpStatusCode.OK);
        browse.ShouldHaveData().Select(p => p.Id).Should().BeEquivalentTo(new[] { keyboard.Id, mouse.Id });

        // 4. The customer orders 1 keyboard + 2 mice with express shipping
        var orderRequest = new CreateOrderRequestBuilder()
            .WithCustomer(c => c.WithName(customer.Profile.Username).WithEmail(customer.Profile.Email))
            .ForProducts((keyboard.Id, 1), (mouse.Id, 2))
            .WithShipping(s => s.WithMethod(ShippingMethod.Express))
            .Build();
        var order = await myOrders.CreateOrderAsync(orderRequest);
        RegisterCleanup(() => _backOffice.DeleteOrderAsync(order.Id));

        order.Status.Should().Be(OrderStatus.Pending);
        order.TotalPrice.Should().Be(49.99m * 1 + 19.50m * 2);
        order.Shipping.Method.Should().Be(ShippingMethod.Express);

        // 5. The order shows up in the customer's order history
        var history = await myOrders.GetOrdersByCustomerEmailAsync(customer.Profile.Email);
        history.ShouldHaveStatusCode(HttpStatusCode.OK);
        history.ShouldHaveData().Select(o => o.Id).Should().Equal(order.Id);

        // 6. Back office confirms and ships the order (partial updates: status only)
        foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Shipped })
        {
            var update = await _backOffice.UpdateOrderAsync(order.Id, new UpdateOrderRequest { Status = status });
            update.ShouldHaveStatusCode(HttpStatusCode.OK);
        }

        // 7. The customer sees the shipment — nothing else about the order changed
        var tracked = await myOrders.GetOrderAsync(order.Id);
        tracked.ShouldHaveStatusCode(HttpStatusCode.OK);
        var shipped = tracked.ShouldHaveData();
        shipped.Status.Should().Be(OrderStatus.Shipped);
        shipped.TotalPrice.Should().Be(order.TotalPrice);
        shipped.Customer.Email.Should().Be(customer.Profile.Email);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("FLOW-SHOP-02: a product removed from the catalog by an admin can no longer be ordered (cross-service consistency)")]
    public async Task ProductRemovedFromCatalog_CanNoLongerBeOrdered()
    {
        var product = await PublishProductAsync($"e2e-{Guid.NewGuid():N}"[..20], price: 10m);
        var customer = await RegisterCustomerAsync();
        var myOrders = new OrderServiceSteps().AsIdentity(customer.Identity);

        // The customer is not allowed to remove products...
        var forbidden = await new ProductServiceSteps().AsIdentity(customer.Identity).DeleteProductAsync(product.Id);
        forbidden.ShouldHaveStatusCode(HttpStatusCode.Forbidden);

        // ...the catalog admin is
        var removed = await _catalogAdmin.DeleteProductAsync(product.Id);
        removed.ShouldHaveStatusCode(HttpStatusCode.NoContent);

        // OrderService validates every line against ProductService
        var response = await myOrders.TryCreateOrderAsync(OrderDataGenerator.GenerateCreateOrderRequest(product.Id));

        response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        response.Content.Should().Contain($"Product with Id {product.Id} not found");
    }

    private async Task<Product> PublishProductAsync(string category, decimal price)
    {
        var product = await _catalogAdmin.CreateProductAsync(
            ProductDataGenerator.GenerateCreateProductRequest(r =>
            {
                r.Category = category;
                r.Price = price;
                r.StockQuantity = 100;
            }));
        // Ignored with 404 when the test already deleted it
        RegisterCleanup(() => _catalogAdmin.DeleteProductAsync(product.Id));
        return product;
    }

    private async Task<(string Identity, UserProfile Profile)> RegisterCustomerAsync()
    {
        var identity = $"customer-{Guid.NewGuid():N}";
        var auth = await _auth.RegisterIdentityAsync(identity, role: "User");
        return (identity, auth.User);
    }
}
