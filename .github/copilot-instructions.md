# Copilot / AI assistant instructions — API Test Framework

## What this repository is

A **product-agnostic .NET 8 API test framework** plus an **example product** that uses it.
Full documentation: `docs/README.md` (architecture, A-to-Z tutorial, reference).

```
framework/src/ApiTestFramework.Core           runtime: ApiClient, RequestBuilder, auth, DI, config, assertions, Allure, BaseTest
framework/src/ApiTestFramework.OpenApi        OpenAPI loader, SchemaExtractor, BuilderScaffolder, TestCaseScaffolder
framework/src/ApiTestFramework.Generator.Cli  generator front-end, configured by a generator.json
framework/tests/ApiTestFramework.Framework.Tests  offline unit tests of the framework
tools/                                         Python tooling (collections, k6, smoke, fuzz, mock), configured by openapi-tools.json
examples/demo-shop/services/                   example microservices (Auth :5300, Product :5100, Order :5200) — system under test only
examples/demo-shop/api-tests/DemoShop.Clients  committed swagger contracts, DTOs, route constants
examples/demo-shop/api-tests/DemoShop.Steps    step classes, generated builders (+ Custom/ partials), data generators
examples/demo-shop/api-tests/DemoShop.Tests    NUnit fixtures, EndToEnd/ flows, GlobalSetup, Generated/ scaffolds (*.cs.txt)
```

## Hard rules

1. **`framework/` and `tools/` must never reference the example** — no service names, routes, ports
   or DemoShop types. Product-specific things are configuration (`appsettings.test.json`,
   `generator.json`, `openapi-tools.json`) or consumer code in `examples/...`.
2. **Tests call steps, never raw HTTP**, except for deliberate anonymous/malformed requests
   (`RequestBuilder...WithoutAuth()`).
3. **Never edit generated files**: `*.g.cs` builders, `Generated/*.cs.txt` scaffolds, `collections/`,
   `perf/k6/`. Change the contract and regenerate:
   `./examples/demo-shop/api-tests/scripts/regenerate-all.ps1`, `python tools/generate_collections.py`,
   `python tools/generate_k6.py`. CI fails on stale generated code.
4. The committed swagger in `DemoShop.Clients/swagger/` is a **curated contract** (required fields,
   401/403, enums as integers). Live swagger is less precise — review diffs after `refresh-swagger.ps1`.
5. DTOs in `DemoShop.Clients/Dtos/` are hand-written mirrors of the contract (namespace
   `DemoShop.Clients.<Service>`, class names = schema names).

## Writing tests

```csharp
[TestFixture]
[AllureSuite("ProductService")]
[AllureFeature("Browse by category")]
public class ProductCategoryTests : BaseTest
{
    private ProductServiceSteps _products = null!;
    private ProductServiceSteps _admin = null!;

    // fixture setup goes into OnFixtureSetUp, never a custom [OneTimeSetUp]
    protected override void OnFixtureSetUp()
    {
        _products = new ProductServiceSteps();
        _admin = _products.AsIdentity(TestIdentities.Admin);    // DELETE is admin-only
    }

    [Test]
    public async Task GetProductsByCategory_ReturnsOnlyActiveProductsOfThatCategory()
    {
        var category = $"cat-{Guid.NewGuid():N}"[..20];         // own your data
        var product = await _products.CreateProductAsync(
            ProductDataGenerator.GenerateCreateProductRequest(r => r.Category = category));
        RegisterCleanup(() => _admin.DeleteProductAsync(product.Id));   // runs in TearDown, LIFO

        var response = await _products.GetProductsByCategoryAsync(category);

        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        response.ShouldHaveData().Select(p => p.Id).Should().Equal(product.Id);
    }
}
```

- Naming: `Operation_Scenario_ExpectedResult`; all tests `async Task`.
- Assertions: `ShouldHaveStatusCode`, `ShouldBeSuccessful`, `ShouldHaveData()` (returns `T`),
  `ShouldMatchDto`, `ShouldMatchDtoExcluding` + FluentAssertions 7.
- Request bodies: generated builders (`new CreateOrderRequestBuilder().ForProducts((id, 2)).WithShipping(s => ...)`)
  or data generators (`OrderDataGenerator.GenerateCreateOrderRequest(productId)`). Builders never invent ids.
- Partial updates in flows: plain DTOs (`new UpdateOrderRequest { Status = OrderStatus.Shipped }`).
- Unique data per test (GUID fragments) — the database is shared and not reset.

## Steps

One class per service in `DemoShop.Steps/ServiceSteps`, client via
`ContainerProvider.ResolveNamed<ApiClient>("<Services key>")`, every request through the private
`Authorized(builder)` helper, `[AllureStep("...")]` on each method. Happy-path steps assert success
and return the entity (`CreateOrderAsync`); `Try...` steps return the raw response for negative tests.
`AsIdentity(name)` / `WithToken(raw)` return a bound copy.

## Authentication & identities

- Mode from `appsettings.test.json` → `Authentication:Mode` (demo: `SessionToken`).
- `GlobalSetup` registers the default user and every `Identities` entry (`admin`, `user`; constants in
  `TestIdentities`). Ad-hoc users: `AuthServiceSteps.RegisterIdentityAsync(name, role)`.
- Per request: `.AsIdentity("admin")`, `.WithBearerToken(raw)`, `.WithoutAuth()` (401 tests).

## Configuration

`DemoShop.Tests/appsettings.test.json`:

```json
{
  "Identities": [ { "Name": "admin", "Role": "Admin" }, { "Name": "user", "Role": "User" } ],
  "Services": {
    "AuthService":    { "BaseUrl": "http://localhost:5300" },
    "ProductService": { "BaseUrl": "http://localhost:5100" },
    "OrderService":   { "BaseUrl": "http://localhost:5200" }
  },
  "Authentication": { "Mode": "SessionToken" },
  "DefaultTimeoutSeconds": 30,
  "RetryCount": 0
}
```

Overrides: `TEST_Services__OrderService__BaseUrl`, `TEST_RetryCount`, `TEST_ENVIRONMENT=<name>`.

## Running

```bash
dotnet test framework/tests/ApiTestFramework.Framework.Tests
docker compose -f examples/demo-shop/docker-compose.yml up -d --build --wait
dotnet test examples/demo-shop/api-tests/DemoShop.Tests [--filter "FullyQualifiedName~EndToEnd"]
```

CI: `.github/workflows/tests.yml` (PRs, pushes to develop, manual runs with an NUnit filter).

## Example services (for context only)

- Nested DTOs: `Product { Price { Amount, Currency }, Inventory { StockQuantity, InStock }, Audit }`,
  `Order { Customer { Name, Email, Address? }, Items [ { ProductId, ProductName, UnitPrice, Quantity, LineTotal } ], Shipping { Method, Address?, Notes? }, TotalPrice, Status }`.
- `DELETE /api/products/{id}` requires role `Admin`; OrderService validates products via ProductService
  forwarding the caller's token. Known demo simplifications are listed in `examples/demo-shop/README.md`.
