# API Test Framework

Complete solution with three .NET 8 microservices and a generic API test framework with JWT authentication.

## Architecture

### Microservices
- **AuthService** (`:5300`) — User registration & login, JWT Bearer token generation
- **ProductService** (`:5100`) — CRUD for products (requires JWT authorization)
- **OrderService** (`:5200`) — CRUD for orders, depends on ProductService for product validation (requires JWT authorization)

### Test Framework
- **ApiTestFramework.Core** — Generic request builder, API client, Autofac DI, Allure integration, FluentAssertions extensions, JWT token management
- **ApiTestFramework.Clients** — NSwag-generated DTOs and placeholder DTOs
- **ApiTestFramework.Steps** — Step classes per service (Bogus data generators, Allure step annotations)
- **ApiTestFramework.Tests** — NUnit test fixtures (22 tests)

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
│   ├── ApiTestFramework.Core/   # Core framework: RequestBuilder, ApiClient, DI, Assertions, TokenProvider, ConfigurationProvider
│   ├── ApiTestFramework.Clients/# NSwag configs + placeholder DTOs (Auth, Product, Order)
│   ├── ApiTestFramework.Steps/  # Step classes + Bogus data generators
│   └── ApiTestFramework.Tests/  # NUnit test fixtures (Auth, Product, Order — 22 tests)
├── docker-compose.yml           # Full stack: microservices + SQL Server + Redis + Consul
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
| ProductService | 7 | Create, get, get all, update, delete (valid & invalid scenarios) |
| OrderService | 8 | Create (valid & invalid product), get, get all, update, delete, full lifecycle |

## CI/CD

The `Jenkinsfile` defines a declarative pipeline that:

1. **Checkout** — clones the repository
2. **Build** — `dotnet build` with `Release` configuration
3. **Start Services** — `docker compose up -d --build --wait` (brings up SQL Server, Redis, Consul + all microservices; waits for all healthchecks)
4. **Run Tests** — `dotnet test` with TRX logger; injects `TEST_Consul__Address=http://consul:8500` so the framework reads service URLs from Consul KV
5. **Publish Results** — publishes the TRX report as a Jenkins test result
6. **Teardown** — `docker compose down -v` always runs (post step)

Each CI run uses an isolated Docker Compose project name (`api-test-${BUILD_NUMBER}`) to allow parallel builds without port conflicts.
