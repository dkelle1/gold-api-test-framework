# GitHub Copilot Instructions — API Test Framework

## Architecture Overview

This solution contains **3 .NET 8 microservices** and a **test automation framework**.

### Microservices (`src/`)

| Service       | Port | Database          | Purpose                          |
|---------------|------|-------------------|----------------------------------|
| AuthService   | 5300 | `AuthServiceDb`   | User registration, JWT tokens    |
| ProductService| 5100 | `ProductServiceDb`| Product CRUD, JWT-protected      |
| OrderService  | 5200 | `OrderServiceDb`  | Order CRUD, cross-calls Product  |

**Infrastructure**: SQL Server (EF Core, `EnsureCreated`) + Redis (distributed cache via `IDistributedCache`).

**Connection strings** are in `appsettings.json` for each service:
```json
"ConnectionStrings": { "DefaultConnection": "Server=localhost;Database=...Db;Trusted_Connection=True;TrustServerCertificate=True" },
"Redis": { "ConnectionString": "localhost:6379" }
```

### Nested DTO Pattern

All API responses use **nested DTOs** (not flat models). The EF entity stays in `Models/`, the response DTO adds structure:

**ProductService** returns `ProductResponse`:
```csharp
ProductResponse { Id, Name, Description, Price (PriceInfo), Inventory (InventoryInfo), Category, IsActive, Audit (AuditInfo) }
PriceInfo       { Amount, Currency }
InventoryInfo   { StockQuantity, InStock }
AuditInfo       { CreatedAt, UpdatedAt? }
```

**OrderService** returns `OrderResponse`:
```csharp
OrderResponse { Id, Product (ProductRef), Customer (CustomerInfo), TotalPrice, Status, Audit (AuditInfo) }
ProductRef    { ProductId, ProductName, UnitPrice, Quantity }
CustomerInfo  { Name, Email }
```

**AuthService** returns `AuthResponse`:
```csharp
AuthResponse { Token (TokenInfo), User (UserProfile) }
TokenInfo    { AccessToken, TokenType, ExpiresAt, ExpiresInSeconds }
UserProfile  { Username, Email, Role }
```

Request DTOs (body) remain **flat**: `CreateProductRequest`, `CreateOrderRequest`, `RegisterRequest`, etc.

---

## Test Framework (`tests/`)

### Project Structure

```
ApiTestFramework.Core    — infrastructure (ApiClient, RequestBuilder, TokenProvider, DI, Assertions, Config, Logging)
ApiTestFramework.Clients — NSwag-generated DTOs and HTTP clients (do not edit by hand)
ApiTestFramework.Steps       — reusable steps (AuthServiceSteps, ProductServiceSteps, OrderServiceSteps) + DataGenerators + UserScope
ApiTestFramework.Tests       — NUnit test fixtures (AuthCrudTests, ProductCrudTests, MultiUserProductTests, OrderCrudTests) — 28 tests
ApiTestFramework.OpenApi     — OpenAPI spec parsing, TestCaseScaffolder, StepsGenerator (generates *ServiceSteps.cs from swagger)
ApiTestFramework.OpenApi.Cli — CLI tool (generate-steps.exe) wrapping StepsGenerator
```

### Key Classes

- **`ApiClient`** — wraps RestSharp, auto-attaches Bearer token from `TokenProvider`, logs all requests to Allure
- **`RequestBuilder`** — fluent builder: `RequestBuilder.Create().WithMethod(Method.Post).WithPath("/api/...").WithBody(dto)`
- **`RequestFactory`** — shortcuts: `RequestFactory.Get(path)`, `RequestFactory.Post(path, body)`, etc.
- **`TokenProvider`** — static holder for JWT; set once in `GlobalSetup.cs`, auto-injected into every request
- **`TestTokenContext`** — `AsyncLocal<string?>` per-test token; overrides `TokenProvider` for the current async execution context
- **`TokenScope`** — synchronous disposable that sets/restores `TestTokenContext`; call in the test method (not inside an async helper) to ensure correct `AsyncLocal` propagation
- **`UserScope`** — registers a fresh user and enters a `TokenScope`; use `UserScope.FromAuthResponse(auth)` synchronously after awaiting the registration
- **`BaseTest`** — base class for all test fixtures; inherit from it; exposes `UseToken(string)` helper
- **`ResponseAssertions`** — extension methods: `.ShouldHaveStatusCode(HttpStatusCode.OK)`, `.ShouldMatchDto(expected)`, `.ShouldHaveData()`
- **`ContainerProvider`** — Autofac DI; use `ContainerProvider.ResolveNamed<ApiClient>("ProductService")`

### How to Add a New Test

1. Create a test class in `tests/ApiTestFramework.Tests/<ServiceName>/`
2. Inherit from `BaseTest`
3. Resolve the step class: `_steps = new ProductServiceSteps()`
4. Use step methods to set up data and make API calls
5. Assert using `ResponseAssertions` extensions

Example:
```csharp
[TestFixture]
public class MyTest : BaseTest
{
    private ProductServiceSteps _steps = null!;

    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _steps = new ProductServiceSteps();
    }

    [Test]
    public async Task MyScenario()
    {
        var product = await _steps.CreateProductAsync();
        var response = await _steps.GetProductAsync(product.Id);
        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        response.ShouldHaveData().Price.Amount.Should().Be(product.Price.Amount);
    }
}
```

### Accessing Nested DTO Fields in Tests

```csharp
// Auth
authResponse.Token.AccessToken      // JWT string
authResponse.User.Username          // username
authResponse.Token.ExpiresAt        // DateTimeOffset

// Product
product.Price.Amount                // decimal price
product.Price.Currency              // "USD"
product.Inventory.StockQuantity     // int
product.Inventory.InStock           // bool
product.Audit.CreatedAt             // DateTimeOffset

// Order
order.Product.ProductId             // int
order.Product.ProductName           // string
order.Product.UnitPrice             // decimal
order.Product.Quantity              // int
order.Customer.Name                 // string
order.Customer.Email                // string
order.TotalPrice                    // decimal
order.Status                        // OrderStatus enum
```

### NSwag Client Generation

DTOs in `tests/ApiTestFramework.Clients/Dtos/` are **generated** from OpenAPI specs — do not edit by hand.

- Offline swagger files: `tests/ApiTestFramework.Clients/swagger/`
- NSwag configs: `nswag-auth.nswag`, `nswag-product.nswag`, `nswag-order.nswag`
- To regenerate clients: run `generate-clients.bat` (requires `dotnet tool install -g NSwag.ConsoleCore`)
- To refresh swagger from live services: run `scripts/refresh-swagger.ps1` (services must be running)

### Per-Test Multi-User Token Isolation

`ApiClient.InjectBearerToken` priority chain (highest wins):
1. Explicit `Authorization` header on the request
2. `TestTokenContext` — `AsyncLocal<string?>` set via `TokenScope.Use()` / `UserScope`
3. Global `TokenProvider`

**Correct pattern** (two-step — `TokenScope.Use` must run in the test method's context):
```csharp
var auth = await _authSteps.RegisterUserAsync("Admin");  // async OK
await using var scope = UserScope.FromAuthResponse(auth); // sync — sets AsyncLocal in THIS context
var product = await _productSteps.CreateProductAsync();   // uses auth.Token
```

**Wrong pattern** (broken — `TokenScope.Use` runs in a child async context and is invisible to the test):
```csharp
// DO NOT DO THIS
await using var scope = await _authSteps.CreateUserScopeAsync();
```

### OpenAPI → ServiceSteps Generator

Generate a full `*ServiceSteps.cs` from any swagger.json:
```bat
generate-steps.bat           :: regenerates all three service step files
```
or:
```
dotnet run --project tests/ApiTestFramework.OpenApi.Cli -- \
  --swagger <path> --service <name> --dto <type> \
  --ns <namespace> --dto-ns <dto-namespace> [--out <file>]
```
Generated files land in `tests/ApiTestFramework.Steps/ServiceSteps/Generated/`.

### Configuration

Test configuration is in `tests/ApiTestFramework.Tests/appsettings.test.json`:
```json
{
  "Consul": { "Address": "http://localhost:8500", "KeyPrefix": "api-test-framework" },
  "Services": {
    "AuthService":    { "BaseUrl": "http://localhost:5300" },
    "ProductService": { "BaseUrl": "http://localhost:5100" },
    "OrderService":   { "BaseUrl": "http://localhost:5200" }
  },
  "DefaultTimeoutSeconds": 30,
  "RetryCount": 0
}
```
Consul KV (when reachable) overrides the JSON values. Set `TEST_Consul__Address=http://consul:8500` in CI.

### Running Tests

Services must be started first:
```powershell
dotnet run --project src/AuthService    --urls http://localhost:5300
dotnet run --project src/ProductService --urls http://localhost:5100
dotnet run --project src/OrderService   --urls http://localhost:5200
```
Then: `dotnet test tests/ApiTestFramework.Tests/`

---

## Code Generation from OpenAPI

The `ApiTestFramework.OpenApi` project provides two layers of generation:

### 1. ServiceSteps generator (implemented)
- `OpenApiSpecLoader.LoadFromFile(path)` + `GetEndpoints(doc)` — parse swagger.json
- `StepsGenerator.GenerateStepsClass(endpoints, serviceName, responseDto, ns, dtoNs)` — emit a full `*ServiceSteps.cs`
- CLI: `tests/ApiTestFramework.OpenApi.Cli` (`generate-steps.exe`)
- Batch script: `generate-steps.bat` in the repo root

### 2. Test-case scaffolder (infrastructure ready)
- `TestCaseScaffolder.GenerateTestCases(endpoints, serviceName)` — derive happy-path / 401 / 404 / 400 test cases
- `TestCaseScaffolder.GenerateCSharpTestClass(cases, namespace)` — emit `Assert.Inconclusive` stubs

Use `StepsGenerator` first to get working step code, then `TestCaseScaffolder` to scaffold test bodies.

---

## Conventions

- All tests are async (`async Task`)
- Test method naming: `MethodName_Scenario_ExpectedResult` (e.g., `CreateProduct_WithValidData_ReturnsCreated`)
- Data generators use **Bogus** library for random but realistic test data
- Every step is annotated with `[AllureStep]` for Allure report hierarchy
- Repository pattern: interface + SQL/Redis implementation; Redis failures are silently swallowed
- Migrations: use EF Core `Database.EnsureCreated()` for dev/test; add migration commands for production
