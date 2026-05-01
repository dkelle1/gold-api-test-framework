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
ApiTestFramework.Steps   — reusable steps (AuthServiceSteps, ProductServiceSteps, OrderServiceSteps) + DataGenerators
ApiTestFramework.Tests   — NUnit test fixtures (AuthCrudTests, ProductCrudTests, OrderCrudTests)
ApiTestFramework.OpenApi — OpenAPI spec parsing + test case scaffolding for future auto-generation
```

### Key Classes

- **`ApiClient`** — wraps RestSharp, auto-attaches Bearer token from `TokenProvider`, logs all requests to Allure
- **`RequestBuilder`** — fluent builder: `RequestBuilder.Create().WithMethod(Method.Post).WithPath("/api/...").WithBody(dto)`
- **`RequestFactory`** — shortcuts: `RequestFactory.Get(path)`, `RequestFactory.Post(path, body)`, etc.
- **`TokenProvider`** — static holder for JWT; set once in `GlobalSetup.cs`, auto-injected into every request
- **`BaseTest`** — base class for all test fixtures; inherit from it
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

### Configuration

Test configuration is in `tests/ApiTestFramework.Tests/appsettings.test.json`:
```json
{
  "AuthService":    { "BaseUrl": "http://localhost:5300" },
  "ProductService": { "BaseUrl": "http://localhost:5100" },
  "OrderService":   { "BaseUrl": "http://localhost:5200" }
}
```

### Running Tests

Services must be started first:
```powershell
dotnet run --project src/AuthService    --urls http://localhost:5300
dotnet run --project src/ProductService --urls http://localhost:5100
dotnet run --project src/OrderService   --urls http://localhost:5200
```
Then: `dotnet test tests/ApiTestFramework.Tests/`

---

## Future: Auto-Generated Tests from OpenAPI

The `ApiTestFramework.OpenApi` project provides infrastructure to:
1. **Parse** swagger.json → `OpenApiSpecLoader.LoadFromFile(path)` + `GetEndpoints(doc)`
2. **Scaffold** test cases → `TestCaseScaffolder.GenerateTestCases(endpoints, serviceName)`
3. **Generate** C# test code → `TestCaseScaffolder.GenerateCSharpTestClass(cases, namespace)`

Use this to bootstrap tests for new endpoints by reading the swagger.json of a service.

---

## Conventions

- All tests are async (`async Task`)
- Test method naming: `MethodName_Scenario_ExpectedResult` (e.g., `CreateProduct_WithValidData_ReturnsCreated`)
- Data generators use **Bogus** library for random but realistic test data
- Every step is annotated with `[AllureStep]` for Allure report hierarchy
- Repository pattern: interface + SQL/Redis implementation; Redis failures are silently swallowed
- Migrations: use EF Core `Database.EnsureCreated()` for dev/test; add migration commands for production

---

## Team Delivery Workflow

Use the workflow skill in `.github/skills/pr-jenkins-workflow/SKILL.md` as the default process.
Use `scripts/jenkins-pr-job-runbook.md` for Jenkins REST API job create/trigger details.

Required sequence:

1. Implement requested changes.
2. Open or update GitHub PR.
3. Run Jenkins PR validation job.
4. Document what changed and test outcome.
5. Update changelog.
6. Leave final merge as a manual user action.
