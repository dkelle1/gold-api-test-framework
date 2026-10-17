# Chapter 7 — End-to-end flows

API tests check one operation at a time. End-to-end flows check that the **business works**:
several services, several actors, a sequence of operations whose effects must add up. Because the
framework already gives you steps per service, builders for every request and identities per
actor, an e2e flow is just a different *composition* of the same building blocks — no second
framework, no duplicated HTTP code.

The finished code is
[`DemoShop.Tests/EndToEnd/CustomerJourneyTests.cs`](../../examples/demo-shop/api-tests/DemoShop.Tests/EndToEnd/CustomerJourneyTests.cs).

In this chapter:

- [Modelling actors as identities](#modelling-actors-as-identities)
- [Writing a customer journey](#writing-a-customer-journey)
- [Checking cross-service consistency](#checking-cross-service-consistency)
- [Keeping journeys isolated](#keeping-journeys-isolated)
- [Running e2e flows separately and against deployed environments](#running-e2e-flows-separately-and-against-deployed-environments)

---

## Modelling actors as identities

### Getting ready

List the actors of the flow and the role each needs. For the shop: a **catalog admin** who
publishes products, a **customer** who buys, and the **back office** that confirms and ships.

### How to do it…

Create one step instance per actor and service:

```csharp
protected override void OnFixtureSetUp()
{
    _auth = new AuthServiceSteps();
    // back-office actors use the "admin" identity provisioned in GlobalSetup
    _catalogAdmin = new ProductServiceSteps().AsIdentity(TestIdentities.Admin);
    _backOffice = new OrderServiceSteps().AsIdentity(TestIdentities.Admin);
}

private async Task<(string Identity, UserProfile Profile)> RegisterCustomerAsync()
{
    var identity = $"customer-{Guid.NewGuid():N}";
    var auth = await _auth.RegisterIdentityAsync(identity, role: "User");   // a fresh customer per journey
    return (identity, auth.User);
}
```

### How it works…

Long-lived roles come from the suite-wide identities ([chapter 6](06-authentication-and-identities.md));
the customer is minted per journey, so the journey owns all of its data. `AsIdentity` makes the
actor explicit at every line: `shop.GetProductsByCategoryAsync(...)` is the customer browsing,
`_backOffice.UpdateOrderAsync(...)` is the back office acting.

---

## Writing a customer journey

### How to do it…

Write the flow as numbered business steps, each asserting what the *business* expects at that
point:

```csharp
[Test]
public async Task NewCustomer_BrowsesOrdersAndReceivesShipment()
{
    // 1. Catalog admin publishes two products in a fresh category
    var category = $"e2e-{Guid.NewGuid():N}"[..20];
    var keyboard = await PublishProductAsync(category, price: 49.99m);
    var mouse = await PublishProductAsync(category, price: 19.50m);

    // 2. A new customer signs up
    var customer = await RegisterCustomerAsync();
    var shop = new ProductServiceSteps().AsIdentity(customer.Identity);
    var myOrders = new OrderServiceSteps().AsIdentity(customer.Identity);

    // 3. The customer browses the category and sees exactly the published products
    var browse = await shop.GetProductsByCategoryAsync(category);
    browse.ShouldHaveData().Select(p => p.Id).Should().BeEquivalentTo(new[] { keyboard.Id, mouse.Id });

    // 4. The customer orders 1 keyboard + 2 mice with express shipping
    var order = await myOrders.CreateOrderAsync(new CreateOrderRequestBuilder()
        .WithCustomer(c => c.WithName(customer.Profile.Username).WithEmail(customer.Profile.Email))
        .ForProducts((keyboard.Id, 1), (mouse.Id, 2))
        .WithShipping(s => s.WithMethod(ShippingMethod.Express))
        .Build());
    RegisterCleanup(() => _backOffice.DeleteOrderAsync(order.Id));
    order.TotalPrice.Should().Be(49.99m * 1 + 19.50m * 2);

    // 5. The order shows up in the customer's order history
    var history = await myOrders.GetOrdersByCustomerEmailAsync(customer.Profile.Email);
    history.ShouldHaveData().Select(o => o.Id).Should().Equal(order.Id);

    // 6. Back office confirms and ships the order (partial updates: status only)
    foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Shipped })
        (await _backOffice.UpdateOrderAsync(order.Id, new UpdateOrderRequest { Status = status }))
            .ShouldHaveStatusCode(HttpStatusCode.OK);

    // 7. The customer sees the shipment — nothing else about the order changed
    var shipped = (await myOrders.GetOrderAsync(order.Id)).ShouldHaveData();
    shipped.Status.Should().Be(OrderStatus.Shipped);
    shipped.TotalPrice.Should().Be(order.TotalPrice);
}
```

### How it works…

Everything here already existed for the API tests — the steps from chapter 4, the order builder
and its `ForProducts` verb from chapter 3, the identities from chapter 6. In the Allure report
the journey reads as the sequence of `[AllureStep]`s, with every HTTP exchange attached, so a
failure points at the business step that broke.

### There's more…

- Use builders for the request the journey is *about* and plain DTOs for intentionally partial
  updates (`new UpdateOrderRequest { Status = … }`): a builder would fill every optional field with
  random data and change more than the step means to.
- Assert invariants at the end (total, customer) — they catch side effects of intermediate steps.

---

## Checking cross-service consistency

### How to do it…

Pick a rule that spans services — *a product removed from the catalog can no longer be ordered*
— and drive it through the public APIs:

```csharp
[Test]
public async Task ProductRemovedFromCatalog_CanNoLongerBeOrdered()
{
    var product = await PublishProductAsync($"e2e-{Guid.NewGuid():N}"[..20], price: 10m);
    var customer = await RegisterCustomerAsync();
    var myOrders = new OrderServiceSteps().AsIdentity(customer.Identity);

    // the customer is not allowed to remove products...
    (await new ProductServiceSteps().AsIdentity(customer.Identity).DeleteProductAsync(product.Id))
        .ShouldHaveStatusCode(HttpStatusCode.Forbidden);

    // ...the catalog admin is
    (await _catalogAdmin.DeleteProductAsync(product.Id)).ShouldHaveStatusCode(HttpStatusCode.NoContent);

    // OrderService validates every line against ProductService
    var response = await myOrders.TryCreateOrderAsync(OrderDataGenerator.GenerateCreateOrderRequest(product.Id));
    response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
    response.Content.Should().Contain($"Product with Id {product.Id} not found");
}
```

### How it works…

The happy-path step (`CreateOrderAsync`) would fail the test at the step; the raw step
(`TryCreateOrderAsync`) hands the response back so the flow can assert the *expected* rejection.

---

## Keeping journeys isolated

### How to do it…

1. **Own your data**: unique categories (`e2e-<guid>`), a fresh customer per journey — never rely
   on what other tests created.
2. **Clean up as you create**, through the actor allowed to:

   ```csharp
   private async Task<Product> PublishProductAsync(string category, decimal price)
   {
       var product = await _catalogAdmin.CreateProductAsync(
           ProductDataGenerator.GenerateCreateProductRequest(r =>
           {
               r.Category = category;
               r.Price = price;
               r.StockQuantity = 100;
           }));
       RegisterCleanup(() => _catalogAdmin.DeleteProductAsync(product.Id));   // a 404 later is fine
       return product;
   }
   ```

3. When several fixtures need the same helpers (`PublishProductAsync`, `RegisterCustomerAsync`),
   move them from the test class into a `Flows/` folder of the Steps project — a "flow" is just a
   step that calls other steps.

### How it works…

Cleanup actions run in `TearDown` in reverse order and never fail the test, so a journey that
deletes a product itself (as in the consistency test) does not break its own cleanup.

---

## Running e2e flows separately and against deployed environments

### How to do it…

1. Run only the flows:

   ```bash
   dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~EndToEnd"
   ```

   or in GitHub Actions: *Run workflow* with filter `FullyQualifiedName~EndToEnd`.

2. Point them at a deployed environment instead of docker-compose:

   ```bash
   export TEST_Services__AuthService__BaseUrl=https://auth.staging.example.com
   export TEST_Services__ProductService__BaseUrl=https://products.staging.example.com
   export TEST_Services__OrderService__BaseUrl=https://orders.staging.example.com
   export TEST_RetryCount=2          # tolerate transient 5xx/timeouts of shared environments
   dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~EndToEnd"
   ```

   or put the same values into `appsettings.test.Staging.json` and set `TEST_ENVIRONMENT=Staging`.

### There's more…

- For a product with a larger e2e suite, create a separate `MyProduct.E2E` test project that
  references the same Clients and Steps projects. API tests and e2e flows then share every step and
  builder but run, report and schedule independently (e.g. API tests on every PR, e2e nightly
  against staging).
- Retries apply only to idempotent operations and to requests that never reached the server, so
  enabling them cannot create duplicate orders ([core reference](../framework/core.md#timeouts-and-retries)).

## See also

- [Chapter 6 — Authentication and identities](06-authentication-and-identities.md)
- [Core — `BaseTest` lifecycle and cleanup](../framework/core.md#test-lifecycle--basetest)
