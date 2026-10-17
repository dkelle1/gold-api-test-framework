using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using DemoShop.Steps.DataGenerators;
using DemoShop.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;

namespace DemoShop.Tests.Authorization;

/// <summary>
/// One shared authorization server, one OAuth2 client per group of services
/// (R-CLIENT-3, R-CLIENT-4). Steps without a binding use their service's own client
/// (Services:&lt;name&gt;:AuthClient); AsClient(...) borrows another group's credentials.
/// </summary>
[TestFixture]
[AllureSuite("Authorization")]
[AllureFeature("Service Group Clients")]
public class ServiceGroupClientTests : BaseTest
{
    private ProductServiceSteps _catalog = null!;
    private OrderServiceSteps _orders = null!;

    protected override void OnFixtureSetUp()
    {
        _catalog = new ProductServiceSteps();   // Catalog client
        _orders = new OrderServiceSteps();      // Orders client
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("R-CLIENT-4: the Catalog client (catalog.admin) manages products end to end without any user account")]
    public async Task CatalogClient_CreatesAndDeletesProducts()
    {
        var product = await _catalog.CreateProductAsync();

        var delete = await _catalog.DeleteProductAsync(product.Id);

        delete.ShouldHaveStatusCode(HttpStatusCode.NoContent);
        (await _catalog.GetProductAsync(product.Id)).ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("R-CLIENT-3: the Orders client places an order; OrderService passes its token on to ProductService, which accepts it (audience catalog)")]
    public async Task OrdersClient_PlacesAnOrder_ItsTokenIsAcceptedByTheCatalog()
    {
        var product = await _catalog.CreateProductAsync();
        RegisterCleanup(() => _catalog.DeleteProductAsync(product.Id));

        var response = await _orders.TryCreateOrderAsync(
            OrderDataGenerator.GenerateCreateOrderRequest(product.Id));

        response.ShouldHaveStatusCode(HttpStatusCode.Created);
        var order = response.ShouldHaveData();
        RegisterCleanup(() => _orders.DeleteOrderAsync(order.Id));
        order.Items.Should().ContainSingle(i => i.ProductId == product.Id);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("R-CLIENT-3: a Catalog token is not valid for OrderService (wrong audience) — 401")]
    public async Task CatalogToken_IsRejectedByOrderService()
    {
        var response = await _orders.AsClient(AuthClients.Catalog).GetAllOrdersAsync();

        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("R-CLIENT-4: the Orders client may read the catalog but not delete products (no catalog.admin) — 403, product unchanged")]
    public async Task OrdersToken_CannotDeleteProducts()
    {
        var product = await _catalog.CreateProductAsync();
        RegisterCleanup(() => _catalog.DeleteProductAsync(product.Id));
        var asOrders = _catalog.AsClient(AuthClients.Orders);

        (await asOrders.GetProductAsync(product.Id)).ShouldHaveStatusCode(HttpStatusCode.OK);
        var delete = await asOrders.DeleteProductAsync(product.Id);

        delete.ShouldHaveStatusCode(HttpStatusCode.Forbidden);
        (await _catalog.GetProductAsync(product.Id)).ShouldHaveStatusCode(HttpStatusCode.OK);
    }
}
