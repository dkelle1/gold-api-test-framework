# API Test Framework

Complete solution with three .NET 8 microservices and a generic API test framework with JWT authentication.

## Architecture

### Microservices
- **AuthService** (`:5300`) — User registration & login, JWT Bearer token generation (tokens carry a role claim)
- **ProductService** (`:5100`) — CRUD for products (requires JWT authorization; `DELETE` additionally requires the `Admin` role)
- **OrderService** (`:5200`) — CRUD for orders, depends on ProductService for product validation (requires JWT authorization)

### Test Framework
- **ApiTestFramework.Core** — Generic request builder, API client, Autofac DI, Allure integration, FluentAssertions extensions, multi-identity JWT token management
- **ApiTestFramework.Clients** — NSwag-generated DTOs and placeholder DTOs
- **ApiTestFramework.Steps** — Step classes per service (Bogus data generators, Allure step annotations)
- **ApiTestFramework.Tests** — NUnit test fixtures (26 tests)

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
| CI/CD | Jenkins (Jenkinsfile) |

## Authentication Flow

All ProductService and OrderService endpoints require a valid JWT Bearer token. The test framework handles this automatically:

1. **GlobalSetup** registers a default user via AuthService and stores the token in `TokenProvider`
2. **GlobalSetup** additionally registers every identity from the `Identities` section of `appsettings.test.json` (e.g. `admin` with role `Admin`) and stores each token in `TokenProvider` under its name
3. **RequestBuilder.Build()** auto-injects the default Bearer token into every request that doesn't specify auth explicitly
4. Individual requests can switch identity via `AsIdentity("admin")`, `WithBearerToken(raw)` or send anonymously via `WithoutAuth()`
5. OrderService forwards the token to ProductService when validating products (cross-service calls)

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
│   ├── ApiTestFramework.Core/   # Core framework: RequestBuilder, ApiClient, DI, Assertions, TokenProvider
│   ├── ApiTestFramework.Clients/# NSwag configs + placeholder DTOs (Auth, Product, Order)
│   ├── ApiTestFramework.Steps/  # Step classes + Bogus data generators
│   └── ApiTestFramework.Tests/  # NUnit test fixtures (Auth, Product, Order — 26 tests)
├── Jenkinsfile                  # CI/CD pipeline
└── ApiTestFramework.sln
```

## Key Framework Features

### Automatic Bearer Token Injection
The `RequestBuilder` automatically attaches the default JWT token from `TokenProvider` to every request. No manual token handling needed in tests:

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

// Skip auth (for 401 tests) — no Authorization header is sent
var response = await client.SendAsync(
    RequestBuilder.Create()
        .WithMethod(Method.Get)
        .WithPath("/api/products")
        .WithoutAuth());
```

### Multiple Identities — Different Tokens per Request
The suite can hold any number of named identities with different permissions and pick one per request. Identities declared in `appsettings.test.json` are registered once in `GlobalSetup`:

```json
"Identities": [
    { "Name": "admin", "Role": "Admin" },
    { "Name": "user",  "Role": "User" }
]
```

Each request (or step instance) chooses its identity explicitly; everything else keeps using the default token:

```csharp
// Request-level: named identity resolved from TokenProvider
var response = await client.SendAsync(
    RequestFactory.Delete("/api/products/42").AsIdentity("admin"));

// Steps-level: bind an identity to a copy of the steps
var forbidden = await _productSteps.AsIdentity("user").DeleteProductAsync(42);   // 403
var deleted   = await _productSteps.AsIdentity("admin").DeleteProductAsync(42);  // 204

// Ad-hoc identity minted at test time (raw token, no registration needed)
var admin = await _authSteps.RegisterUserAsync(AuthDataGenerator.GenerateRegisterRequest(role: "Admin"));
await _productSteps.WithToken(admin.Token.AccessToken).DeleteProductAsync(42);

// Or register a named identity on the fly and reuse it anywhere
await _authSteps.RegisterIdentityAsync("auditor", role: "User");
await client.SendAsync(RequestFactory.Get("/api/orders").AsIdentity("auditor"));
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
| ProductService | 11 | Create, get, get all, update, delete (valid & invalid scenarios) + role-based authorization (admin vs user vs anonymous) |
| OrderService | 8 | Create (valid & invalid product), get, get all, update, delete, full lifecycle |
