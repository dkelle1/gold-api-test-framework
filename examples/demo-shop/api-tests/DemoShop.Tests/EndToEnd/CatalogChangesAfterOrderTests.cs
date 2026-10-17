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
/// Customers are charged what a product cost when they ordered it; price changes and deactivation
/// affect only what happens next, never orders that are already placed.
/// Flow: examples/demo-shop/api-tests/flows/catalog-changes-after-order.md (FLOW-SHOP-03)
/// </summary>
[TestFixture]
[AllureSuite("End-to-end")]
[AllureFeature("Catalog changes")]
public class CatalogChangesAfterOrderTests : BaseTest
{
    private AuthServiceSteps _auth = null!;
    private ProductServiceSteps _catalogAdmin = null!;
    private OrderServiceSteps _backOffice = null!;

    protected override void OnFixtureSetUp()
    {
        _auth = new AuthServiceSteps();
        _catalogAdmin = new ProductServiceSteps().AsIdentity(TestIdentities.Admin);
        _backOffice = new OrderServiceSteps().AsIdentity(TestIdentities.Admin);   // cleanup of orders
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("FLOW-SHOP-03: price changes and deactivation never alter placed orders, but apply to new ones")]
    public async Task CatalogChanges_DoNotAlterPlacedOrders_ButApplyToNewOnes()
    {
        // 1. Catalog admin publishes the desk lamp at 100.00
        var category = $"flow3-{Guid.NewGuid():N}"[..20];
        var lamp = await _catalogAdmin.CreateProductAsync(ProductDataGenerator.GenerateCreateProductRequest(r =>
        {
            r.Name = "Desk lamp";
            r.Category = category;
            r.Price = 100.00m;
            r.StockQuantity = 50;
        }));
        RegisterCleanup(() => _catalogAdmin.DeleteProductAsync(lamp.Id));
        lamp.IsActive.Should().BeTrue();
        lamp.Price.Amount.Should().Be(100.00m);

        // 2. Customer registers                                                      (R-AUTH-1)
        var customerIdentity = $"customer-{Guid.NewGuid():N}";
        var customer = (await _auth.RegisterIdentityAsync(customerIdentity, role: "User")).User;
        var shop = new ProductServiceSteps().AsIdentity(customerIdentity);
        var myOrders = new OrderServiceSteps().AsIdentity(customerIdentity);

        // 3. Customer orders 2 desk lamps: unit 100.00, line 200.00, total 200.00   (R-ORD-4)
        var orderA = await PlaceOrderAsync(myOrders, customer, lamp.Id, quantity: 2);
        orderA.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { ProductId = lamp.Id, UnitPrice = 100.00m, Quantity = 2, LineTotal = 200.00m });
        orderA.TotalPrice.Should().Be(200.00m);

        // 4. Catalog admin raises the price to 150.00                                (R-PROD-5)
        var repriced = await _catalogAdmin.UpdateProductAsync(lamp.Id, new UpdateProductRequest { Price = 150.00m });
        repriced.ShouldHaveStatusCode(HttpStatusCode.OK);
        repriced.ShouldHaveData().Price.Amount.Should().Be(150.00m);

        // 5. Customer opens order A: still 100.00 / 200.00                           (R-ORD-3)
        var orderAAfterRepricing = (await myOrders.GetOrderAsync(orderA.Id))
            .ShouldHaveStatusCode(HttpStatusCode.OK).ShouldHaveData();
        orderAAfterRepricing.Items.Single().UnitPrice.Should().Be(100.00m);
        orderAAfterRepricing.TotalPrice.Should().Be(200.00m);

        // 6. Customer orders 1 desk lamp at the new price: 150.00 / 150.00          (R-ORD-3, R-ORD-4)
        var orderB = await PlaceOrderAsync(myOrders, customer, lamp.Id, quantity: 1);
        orderB.Items.Single().UnitPrice.Should().Be(150.00m);
        orderB.TotalPrice.Should().Be(150.00m);

        // 7. Catalog admin deactivates the lamp; it stays readable by id, inactive   (R-PROD-4)
        (await _catalogAdmin.UpdateProductAsync(lamp.Id, new UpdateProductRequest { IsActive = false }))
            .ShouldHaveStatusCode(HttpStatusCode.OK);
        var deactivated = (await shop.GetProductAsync(lamp.Id))
            .ShouldHaveStatusCode(HttpStatusCode.OK).ShouldHaveData();
        deactivated.IsActive.Should().BeFalse();

        // 8. Customer browses the category: the lamp is no longer listed             (R-PROD-3)
        var browse = await shop.GetProductsByCategoryAsync(category);
        browse.ShouldHaveStatusCode(HttpStatusCode.OK);
        browse.ShouldHaveData().Should().NotContain(p => p.Id == lamp.Id);

        // 9. Customer tries to order the lamp: rejected with 400, nothing created   (R-ORD-2)
        var rejected = await myOrders.TryCreateOrderAsync(OrderRequestFor(customer, lamp.Id, quantity: 1));
        rejected.ShouldHaveStatusCode(HttpStatusCode.BadRequest);

        // 10. Order history: exactly A and B, totals 200.00 and 150.00               (R-ORD-3, R-ORD-8)
        var history = (await myOrders.GetOrdersByCustomerEmailAsync(customer.Email))
            .ShouldHaveStatusCode(HttpStatusCode.OK).ShouldHaveData();
        history.Select(o => (o.Id, o.TotalPrice)).Should().BeEquivalentTo(new[]
        {
            (orderA.Id, 200.00m),
            (orderB.Id, 150.00m)
        });
    }

    private async Task<Order> PlaceOrderAsync(OrderServiceSteps orders, UserProfile customer, int productId, int quantity)
    {
        var order = await orders.CreateOrderAsync(OrderRequestFor(customer, productId, quantity));
        RegisterCleanup(() => _backOffice.DeleteOrderAsync(order.Id));
        return order;
    }

    private static CreateOrderRequest OrderRequestFor(UserProfile customer, int productId, int quantity)
        => new CreateOrderRequestBuilder()
            .WithCustomer(c => c.WithName(customer.Username).WithEmail(customer.Email))
            .ForProduct(productId, quantity)
            .Build();
}
