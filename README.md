# API Test Framework

Complete solution with three .NET 8 microservices and a generic API test framework with JWT authentication.

## Architecture

### Microservices
- **AuthService** (`:5300`) — User registration & login, JWT Bearer token generation
- **ProductService** (`:5100`) — CRUD for products (requires JWT authorization)
- **OrderService** (`:5200`) — CRUD for orders, depends on ProductService for product validation (requires JWT authorization)

### Test Framework
- **ApiTestFramework.Core** — Generic request builder, API client, Autofac DI, Allure integration, FluentAssertions extensions, JWT token management, per-test token context (`TestTokenContext` / `TokenScope`)
- **ApiTestFramework.Clients** — NSwag-generated DTOs and placeholder DTOs
- **ApiTestFramework.Steps** — Step classes per service (Bogus data generators, Allure step annotations), `UserScope` for per-test user isolation
- **ApiTestFramework.Tests** — NUnit test fixtures (28 tests across 4 fixtures)
- **ApiTestFramework.OpenApi** — OpenAPI spec loader, test-case scaffolder, and `StepsGenerator` for code generation
- **ApiTestFramework.OpenApi.Cli** — CLI wrapper (`generate-steps.exe`) that drives `StepsGenerator` from the command line

## Tech Stack
| Component | Technology |
|-----------|-----------|
| Microservices | .NET 8 Minimal API, Swagger |
| Authentication | JWT Bearer (shared symmetric key) |
| Test Runner | NUnit 4 |
| HTTP Client | RestSharp |
| DI Container | Autofac |
| DTO Generation | NSwag |
| Test Data | Bogus |
| Assertions | FluentAssertions |
| Reporting | Allure (with full request/response logging) |
| Configuration | HashiCorp Consul KV (optional, overrides JSON) |
| CI/CD | Jenkins (Jenkinsfile) |
| Infrastructure | Docker Compose (SQL Server, Redis, Consul) |

## Authentication Flow

All ProductService and OrderService endpoints require a valid JWT Bearer token. The test framework handles this automatically:

1. **GlobalSetup** registers a user via AuthService and stores the token in `TokenProvider`
2. **RequestBuilder.Build()** auto-injects the Bearer token from `TokenProvider` into every request
3. OrderService forwards the token to ProductService when validating products (cross-service calls)

```
┌──────────────┐     register/login      ┌──────────────┐
│  Test Runner  │ ──────────────────────► │  AuthService  │
│  (GlobalSetup)│ ◄────── JWT token ──── │   :5300       │
└──────┬───────┘                         └──────────────┘
       │ token stored in TokenProvider
       │
       ├── requests with Bearer token ──► ProductService :5100
       │
       └── requests with Bearer token ──► OrderService :5200
                                                │
                                     forwards token to ProductService
```

## Quick Start

### Option A — Docker Compose (recommended)

Starts all microservices, SQL Server, Redis, and Consul in one command.  
The `consul-init` sidecar seeds service base-URLs into Consul KV so the test framework picks them up automatically.

```bash
docker compose up -d --build --wait
dotnet build tests/ApiTestFramework.Tests
export TEST_Consul__Address=http://localhost:8500   # (PowerShell: $env:TEST_Consul__Address=...)
dotnet test tests/ApiTestFramework.Tests
```

### Option B — Manual (no Docker)

### 1. Start the microservices
```bash
# Terminal 1 — AuthService (must start first)
dotnet run --project src/AuthService

# Terminal 2
dotnet run --project src/ProductService

# Terminal 3
dotnet run --project src/OrderService
```

### 2. Verify Swagger UI
- AuthService: http://localhost:5300
- ProductService: http://localhost:5100
- OrderService: http://localhost:5200

### 3. (Optional) Generate NSwag clients
```bash
cd tests/ApiTestFramework.Clients
# Requires: dotnet tool install -g NSwag.ConsoleCore
nswag run nswag-product.nswag /runtime:Net80
nswag run nswag-order.nswag /runtime:Net80
```

### 4. Run tests
```bash
dotnet test tests/ApiTestFramework.Tests
```

### 5. View Allure report
```bash
# Requires allure CLI (install via: scoop install allure)
allure serve TestResults/allure-results
```

## Project Structure
```
├── src/
│   ├── AuthService/             # Auth microservice (port 5300) — JWT token issuer
│   ├── ProductService/          # Product microservice (port 5100, JWT protected)
│   └── OrderService/            # Order microservice (port 5200, JWT protected, depends on ProductService)
├── tests/
│   ├── ApiTestFramework.Core/   # Core framework: RequestBuilder, ApiClient, DI, Assertions, TokenProvider, ConfigurationProvider, TestTokenContext, TokenScope
│   ├── ApiTestFramework.Clients/# NSwag configs + placeholder DTOs (Auth, Product, Order)
│   ├── ApiTestFramework.Steps/  # Step classes + Bogus data generators + UserScope (per-test user isolation)
│   ├── ApiTestFramework.Tests/  # NUnit test fixtures (Auth, Product, Order — 28 tests)
│   ├── ApiTestFramework.OpenApi/# OpenAPI loader, TestCaseScaffolder, StepsGenerator
│   └── ApiTestFramework.OpenApi.Cli/ # CLI (generate-steps.exe) to generate *ServiceSteps.cs from swagger
├── docker-compose.yml           # Full stack: microservices + SQL Server + Redis + Consul
├── generate-steps.bat           # Convenience script — regenerates all service step files from swagger
├── Jenkinsfile                  # CI/CD pipeline
└── ApiTestFramework.sln
```

## Configuration

Test configuration is resolved in priority order (highest wins):

1. **Consul KV** — `api-test-framework` key (when `TEST_Consul__Address` is set or `Consul.Address` in JSON is non-empty)
2. **Environment variables** — prefix `TEST_`, double-underscore notation (e.g. `TEST_Services__AuthService__BaseUrl`)
3. **`appsettings.test.json`** — local fallback, always present

### Consul KV

When Consul is available the framework reads a single JSON document from key `api-test-framework`.  
To disable Consul entirely, set `"Consul": { "Address": "" }` in `appsettings.test.json` — the framework falls back to JSON-only automatically.

**To add a new service:**
1. Add an entry under `"Services"` in `appsettings.test.json` (fallback)
2. Update the JSON payload in the `consul-init` entrypoint in `docker-compose.yml`

No C# code changes required.

### `appsettings.test.json` structure
```json
{
  "Consul": {
    "Address": "http://localhost:8500",
    "KeyPrefix": "api-test-framework"
  },
  "Services": {
    "AuthService":    { "BaseUrl": "http://localhost:5300" },
    "ProductService": { "BaseUrl": "http://localhost:5100" },
    "OrderService":   { "BaseUrl": "http://localhost:5200" }
  },
  "DefaultTimeoutSeconds": 30,
  "RetryCount": 0
}
```

## Key Framework Features

### Automatic Bearer Token Injection
The `RequestBuilder` automatically attaches the JWT token from `TokenProvider` to every request. No manual token handling needed in tests:

```csharp
// Token is auto-injected — no need to call WithBearerToken()
var response = await client.SendAsync<Product>(
    RequestFactory.Get("/api/products"));

// Explicit token override is still possible
var response = await client.SendAsync<Product>(
    RequestBuilder.Create()
        .WithMethod(Method.Get)
        .WithPath("/api/products")
        .WithBearerToken("custom-token"));

// Skip auth (for 401 tests) — set empty Authorization header
var response = await client.SendAsync(
    RequestBuilder.Create()
        .WithMethod(Method.Get)
        .WithPath("/api/products")
        .WithHeader("Authorization", ""));
```

### Request Builder
```csharp
// Simple GET
var response = await client.SendAsync<Product>(
    RequestFactory.Get("/api/products"));

// POST with body
var response = await client.SendAsync<Product>(
    RequestFactory.Post("/api/products", createRequest));

// Full builder pattern
var response = await client.SendAsync<Product>(
    RequestBuilder.Create()
        .WithMethod(Method.Get)
        .WithPath("/api/products/{id}")
        .WithPathSegment("id", 42)
        .WithHeader("X-Custom", "value")
        .WithQueryParameter("filter", "active")
        .WithTimeout(15));
```

### Allure Reporting
Every request/response is automatically attached to the Allure report. On assertion failures, detailed failure info including the response body is logged. DTO comparison results (expected vs actual) are attached for full visibility.

### Steps Usage
```csharp
// AuthServiceSteps — get a token
var token = await _authSteps.GetBearerTokenAsync();
var authResponse = await _authSteps.RegisterUserAsync();
var loginResponse = await _authSteps.LoginAsync("username", "password");

// ProductServiceSteps — create test data
var product = await _productSteps.CreateProductAsync();

// OrderServiceSteps — full setup with dependency
var (order, productId) = await _orderSteps.CreateOrderWithProductAsync();
```

### Per-Test Multi-User Token Isolation

Every test runs under the global token set in `GlobalSetup`. When a test needs a different user (e.g. to verify role-based access or token ownership), use `UserScope` to register a fresh user and scope its token to the current test without affecting other tests running in parallel.

`UserScope` is backed by `TestTokenContext` (`AsyncLocal<string?>`). Because `AsyncLocal<T>` propagates changes **down** into child continuations but **not back up** to the caller, the two-step pattern is required:

```csharp
// Step 1 — async: register the user (runs in a child async context)
var auth = await _authSteps.RegisterUserAsync("Admin");

// Step 2 — sync: enter the scope IN THIS METHOD'S execution context
//   UserScope.FromAuthResponse is synchronous, so TestTokenContext.SetToken()
//   runs here and is visible to all subsequent awaits in this test.
await using var scope = UserScope.FromAuthResponse(auth);

// All requests inside the scope use auth.Token.AccessToken
var product = await _productSteps.CreateProductAsync();

// Scope is restored to previous token automatically on dispose
```

> **Why not `await using var scope = await CreateUserScopeAsync()`?**
> Calling `TokenScope.Use()` (which sets `TestTokenContext`) inside an async helper after an `await` runs in a child execution context. `AsyncLocal<T>` changes in a child context don't flow back to the parent, so the token would never be set in the test method's context. The two-step pattern avoids this.

You can also override the token directly without creating a new user:
```csharp
// Scopes an existing token for the duration of the using block
using var _ = UseToken("eyJhbGci...");
```

### OpenAPI → ServiceSteps Code Generator

The framework can generate complete `*ServiceSteps.cs` files from any OpenAPI/swagger.json spec:

```bat
:: Regenerate all three service step files at once
generate-steps.bat

:: Or generate a single service
dotnet run --project tests/ApiTestFramework.OpenApi.Cli -- ^
  --swagger tests/ApiTestFramework.Clients/swagger/order-swagger.json ^
  --service Order --dto Order ^
  --ns ApiTestFramework.Steps.ServiceSteps.Generated ^
  --dto-ns ApiTestFramework.Clients.OrderService ^
  --out tests/ApiTestFramework.Steps/ServiceSteps/Generated/OrderServiceSteps.g.cs
```

**What the generator produces per endpoint:**
- `GET /collection` → `List<T>` happy-path method asserting `200 OK`
- `GET /resource/{id}` → `T` method
- `POST` → `T` method asserting `201 Created` + `TryCreateAsync` raw-response overload
- `PUT` / `DELETE` → typed method + `Try*Async` overload for negative-test scenarios
- `[AllureStep]` annotation on every method
- Constructor resolves `ApiClient` via `ContainerProvider`

Generated files land in `tests/ApiTestFramework.Steps/ServiceSteps/Generated/` and are committed alongside hand-written steps as a reference/bootstrap. They can be used directly or promoted to hand-written steps by copying to the `ServiceSteps/` folder and customising.

The offline swagger specs are in `tests/ApiTestFramework.Clients/swagger/`. To refresh them from live services, run `scripts/refresh-swagger.ps1` (services must be running).

### DTO Validation
```csharp
// Full DTO comparison
response.ShouldMatchDto(expectedProduct, "Product comparison");

// Exclude specific properties
response.ShouldMatchDtoExcluding(expectedProduct,
    config => config.Excluding(p => p.UpdatedAt),
    "Product comparison excluding UpdatedAt");
```

## Test Suites

| Suite | Tests | Description |
|-------|-------|-------------|
| AuthService | 7 | Register, login, duplicate user, wrong password, unauthorized access |
| ProductService (CRUD) | 7 | Create, get, get all, update, delete (valid & invalid scenarios) |
| ProductService (Multi-user) | 6 | Per-test user isolation, token scoping, scope restore, `UseToken` override |
| OrderService | 8 | Create (valid & invalid product), get, get all, update, delete, full lifecycle |
| **Total** | **28** | |

## CI/CD

The `Jenkinsfile` defines a declarative pipeline that:

1. **Checkout** — clones the repository
2. **Build** — `dotnet build` with `Release` configuration
3. **Start Services** — `docker compose up -d --build --wait` (brings up SQL Server, Redis, Consul + all microservices; waits for all healthchecks)
4. **Run Tests** — `dotnet test` with TRX logger; injects `TEST_Consul__Address=http://consul:8500` so the framework reads service URLs from Consul KV
5. **Publish Results** — publishes the TRX report as a Jenkins test result
6. **Teardown** — `docker compose down -v` always runs (post step)

Each CI run uses an isolated Docker Compose project name (`api-test-${BUILD_NUMBER}`) to allow parallel builds without port conflicts.
