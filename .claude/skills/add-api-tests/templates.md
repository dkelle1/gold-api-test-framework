# Templates for add-api-tests

Real, compiling references: `DemoShop.Steps/ServiceSteps/ProductServiceSteps.cs` and
`DemoShop.Tests/ProductService/ProductCategoryTests.cs`.

## Step method

```csharp
// read: return the raw response — the test decides what "correct" means
[AllureStep("Get products in category: {category}")]
public async Task<RestResponse<List<Product>>> GetProductsByCategoryAsync(string category)
{
    var response = await _client.SendAsync<List<Product>>(
        Authorized(RequestBuilder.Create()
            .WithMethod(Method.Get)
            .WithPath(ProductServiceRoutes.ByCategory)
            .WithPathSegment("category", category)));

    return response;
}

// create, happy path: assert success, return the entity (for Arrange sections and e2e flows)
[AllureStep("Create an order")]
public async Task<Order> CreateOrderAsync(CreateOrderRequest createRequest)
{
    var response = await _client.SendAsync<Order>(
        Authorized(RequestFactory.Post(OrderServiceRoutes.Base, createRequest)));
    response.ShouldHaveStatusCode(HttpStatusCode.Created);
    return response.ShouldHaveData();
}

// create, raw: for negative tests
[AllureStep("Attempt to create an order (raw response)")]
public async Task<RestResponse<Order>> TryCreateOrderAsync(CreateOrderRequest createRequest)
    => await _client.SendAsync<Order>(
        Authorized(RequestFactory.Post(OrderServiceRoutes.Base, createRequest)));
```

## Fixture

```csharp
[TestFixture]
[AllureSuite("ProductService")]
[AllureFeature("Browse by category")]
public class ProductCategoryTests : BaseTest
{
    private ProductServiceSteps _products = null!;
    private ProductServiceSteps _admin = null!;

    protected override void OnFixtureSetUp()
    {
        _products = new ProductServiceSteps();
        _admin = _products.AsIdentity(TestIdentities.Admin);   // DELETE is admin-only
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    public async Task GetProductsByCategory_ReturnsOnlyActiveProductsOfThatCategory()
    {
        // Arrange — unique data keeps assertions exact on a shared database
        var category = $"cat-{Guid.NewGuid():N}"[..20];
        var active = await CreateProductAsync(category);

        // Act
        var response = await _products.GetProductsByCategoryAsync(category);

        // Assert
        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        response.ShouldHaveData().Select(p => p.Id).Should().Equal(active.Id);
    }

    [Test]
    public async Task GetProductsByCategory_WithoutToken_ReturnsUnauthorized()
    {
        var client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");
        var response = await client.SendAsync(
            RequestBuilder.Create().WithMethod(Method.Get)
                .WithPath(ProductServiceRoutes.ByCategory).WithPathSegment("category", "x")
                .WithoutAuth());
        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    private async Task<Product> CreateProductAsync(string category)
    {
        var product = await _products.CreateProductAsync(
            ProductDataGenerator.GenerateCreateProductRequest(r => r.Category = category));
        RegisterCleanup(() => _admin.DeleteProductAsync(product.Id));
        return product;
    }
}
```

## Negative request bodies

```csharp
var invalid = new CreateOrderRequestBuilder()
    .ForProduct(product.Id)
    .With(r => r.Customer.Email = "")      // escape hatch for invalid values
    .Build();
var response = await _orders.TryCreateOrderAsync(invalid);
response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
```
