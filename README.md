# API Test Framework

Complete solution with three .NET 8 microservices and a generic API test framework with JWT authentication.

## Architecture

### Microservices
- **AuthService** (`:5300`) — User registration & login, JWT Bearer token generation
- **ProductService** (`:5100`) — CRUD for products (requires JWT authorization)
- **OrderService** (`:5200`) — CRUD for orders, depends on ProductService for product validation (requires JWT authorization)

### Test Framework
- **ApiTestFramework.Core** — Generic request builder, API client, Autofac DI, Allure integration, FluentAssertions extensions, JWT token management
- **ApiTestFramework.Clients** — NSwag-generated DTOs and placeholder DTOs + offline swagger files
- **ApiTestFramework.Steps** — Step classes per service, generated fluent test-data builders (+ partial customizations), Bogus data generators
- **ApiTestFramework.OpenApi** — OpenAPI spec loader, schema extractor, builder/test-case/negative-case scaffolders, swagger drift checker, response schema validator
- **ApiTestFramework.OpenApi.Tests** — unit tests of the generator itself (43 tests, no services needed)
- **ApiTestFramework.Infrastructure** — Testcontainers stack (SQL Server + Redis + the three services built from their Dockerfiles)
- **ApiTestFramework.Generator.Cli** — console tool: regenerates builders + scaffolds, checks swagger drift (`check-drift`)
- **ApiTestFramework.Tests** — NUnit test fixtures (27 hand-written + 19 generated at runtime from swagger)

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

### Zero-setup (Docker required)

```bash
dotnet build ApiTestFramework.sln
dotnet test tests/ApiTestFramework.Tests
```

That's it. In the default `Auto` infrastructure mode the suite probes the
configured service URLs; when nothing is running it starts the whole stack
itself with **Testcontainers** — SQL Server, Redis and the three services
built from their Dockerfiles on a private network — and tears it down after
the run (the Ryuk reaper cleans up even if the process dies). The first run
builds the service images; later runs reuse the Docker cache.

Modes (`Infrastructure:Mode` in `appsettings.test.json` or env var `TEST_Infrastructure__Mode`):
- `Auto` (default) — use running services if reachable, else Testcontainers
- `External` — always use configured URLs (CI runs with docker compose)
- `TestContainers` — always start a fresh containerized stack

### Manual setup (alternative)

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
│   ├── ApiTestFramework.Core/          # Core framework: RequestBuilder, ApiClient, DI, Assertions, TokenProvider
│   ├── ApiTestFramework.Clients/       # NSwag configs + placeholder DTOs + offline swagger/*.json
│   ├── ApiTestFramework.Steps/         # Step classes + data generators
│   │   └── Builders/
│   │       ├── <Service>/Generated/    # *.g.cs — fluent builders regenerated from swagger (do not edit)
│   │       └── Custom/                 # hand-written partial halves (survive regeneration)
│   ├── ApiTestFramework.OpenApi/       # OpenAPI loader, SchemaExtractor, BuilderScaffolder, TestCaseScaffolder
│   ├── ApiTestFramework.Generator.Cli/ # dotnet run → regenerates builders + test scaffolds
│   └── ApiTestFramework.Tests/         # NUnit test fixtures (Auth, Product, Order — 25 tests)
│       └── Generated/                  # *.cs.txt test scaffolds (promote to .cs by hand)
├── scripts/
│   ├── refresh-swagger.ps1             # pull swagger.json from running services
│   └── regenerate-all.ps1              # full regeneration pipeline
├── Jenkinsfile                         # CI/CD pipeline
└── ApiTestFramework.sln
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

### Test Data Builders — nested DTOs in a single request

`CreateOrderRequest` is deliberately deeply nested (`Customer → Address`, `Items[]`, `Shipping → Address`).
The generated fluent builders mirror that nesting: every object property gets an
`Action<...Builder>` overload and every collection gets `AddX(...)` methods, so one
request composed of many DTOs reads top-down:

```csharp
using ApiTestFramework.Steps.Builders.OrderService;

var request = new CreateOrderRequestBuilder()          // sensible Bogus defaults everywhere
    .WithCustomer(c => c
        .WithName("Jan Testowy")
        .WithAddress(a => a.WithCity("Gdańsk").WithPostalCode("80-001")))
    .WithShipping(s => s.WithMethod(ShippingMethod.Express))
    .ForProduct(productId, quantity: 2)                // custom helper (partial class)
    .ForProduct(otherProductId, quantity: 3)           // multi-item order
    .Build();
```

Builder anatomy:
- `Builders/<Service>/Generated/*.g.cs` — regenerated from swagger; Bogus defaults are
  derived from property name/type heuristics (emails, addresses, prices, quantities…).
  Ids are never invented — tests must supply them (`WithProductId`, `ForProduct`).
- `Builders/Custom/*.cs` — the hand-written half of the same `partial` class. Domain
  knowledge lives here (e.g. password policy, `ForProduct` helpers) and hooks into the
  generated code via `OnDefaultsApplied()` / `OnBeforeBuild(instance)`. Regeneration
  never touches these files.
- `DataGenerators/*` — thin facades over the builders for the most common cases.

### Updating endpoints / test data — regeneration workflow

When a service endpoint or DTO changes, regenerate instead of hand-editing:

```powershell
# 1. Change the service (src/...), start the services
# 2. Full pipeline: swagger → NSwag DTOs → builders + scaffolds → build
.\scripts\regenerate-all.ps1 -RefreshSwagger -NSwag

# Offline (no running services; swagger files edited by hand or from CI):
.\scripts\regenerate-all.ps1
```

What gets regenerated where:

| Artifact | Location | Source | Editable? |
|----------|----------|--------|-----------|
| Swagger specs | `Clients/swagger/*.json` | running services (`refresh-swagger.ps1`) | no — refresh |
| DTO clients | `Clients/Dtos` (NSwag) | swagger | no — regenerate |
| Builders | `Steps/Builders/<Service>/Generated/*.g.cs` | swagger request schemas | no — regenerate |
| Builder customizations | `Steps/Builders/Custom/*.cs` | hand-written | yes (partial classes) |
| Test scaffolds | `Tests/Generated/*.cs.txt` | swagger endpoints | promote to `.cs` by hand |

The generator (`ApiTestFramework.Generator.Cli`) walks each swagger file, takes the
transitive closure of schemas reachable from request bodies (nested objects, arrays,
nullable `oneOf` wrappers), and emits one partial builder class per schema. Stale
`*.g.cs` files are deleted, so removed DTOs disappear on regeneration; custom partial
files fail the post-regeneration build if they reference removed members — which is
exactly the signal that a hand-written helper needs updating.

### Test isolation & parallelism

Every resource a step creates (product, order) is registered in `TestDataRegistry`
and deleted automatically in `BaseTest.TearDown` — LIFO, so orders go before their
products, and a 404 during cleanup (test deleted it itself) is ignored. Tests leave
the database the way they found it, which enables parallel execution:
fixtures run in parallel (`ParallelScope.Fixtures`, 4 workers).

### Schema-driven negative tests

`OrderServiceNegativeTests` has no hand-written cases: at run time,
`NegativeCaseGenerator` reads `swagger/order-swagger.json`, builds a minimal valid
payload, and emits one mutation per `required` constraint (missing property at any
nesting depth, empty required array) — each expected to return 400. Add a `required`
field to the contract and a new negative test appears by itself.

### Authorization matrix

`UnauthorizedMatrixTests` enumerates every endpoint that declares Bearer security in
any swagger file and asserts it returns 401 without a token. A new protected endpoint
is covered automatically; one that loses its security requirement drops out here and
gets caught by the swagger drift gate instead.

### Response contract validation

`ResponseContractTests` fetch raw JSON from the live services and validate it
field-by-field against the swagger response schemas (`JsonSchemaValidator`):
undeclared properties, missing non-nullable fields and type mismatches are
violations at any nesting depth. Typed deserialization would silently ignore
all of these. Together with the drift gate this closes the loop:
spec ⇄ service (drift check) and wire format ⇄ spec (contract tests).

### Diagnostics

- Every request carries an `X-Test-Id` header (the NUnit test full name); services log
  it, so `docker compose logs` can be correlated with a specific failing test.
- Every Allure request attachment ends with a ready-to-paste **cURL reproduction**
  (Bearer token masked).

### CI quality gates (Jenkinsfile)

| Stage | Gate |
|-------|------|
| Framework Unit Tests | generator/extractor/drift-checker unit tests (no services needed) |
| Verify Generated Code | regenerates builders + scaffolds and fails on `git diff` — committed `*.g.cs` must match committed swagger |
| Swagger Drift Check | downloads live swagger from the running services and structurally compares it with the committed files (`check-drift`); hard drift fails the build, nullable/required differences are warnings |

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
| OrderService | 11 | Create (single/multi-item, nested address+shipping, invalid product, empty items), get, get all, update (nested customer), delete, full lifecycle |
| OrderService negative (generated) | 7 | Required-constraint mutations derived from the swagger schema, expected 400 |
| Security matrix (generated) | 12 | Every Bearer-protected endpoint × no token → 401 |
| Contracts | 2 | Raw Order/Product responses validated field-by-field against swagger response schemas |
| Framework unit tests | 43 | SchemaExtractor, BuilderScaffolder, NegativeCaseGenerator, SwaggerDriftChecker, JsonSchemaValidator (separate project, no services) |
