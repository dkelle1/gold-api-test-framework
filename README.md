# API Test Framework

[![Tests](https://github.com/dkelle1/gold-api-test-framework/actions/workflows/tests.yml/badge.svg?branch=develop)](https://github.com/dkelle1/gold-api-test-framework/actions/workflows/tests.yml)

Complete solution with three .NET 8 microservices and a generic API test framework with pluggable authentication.

> **Reusing this framework for another product/company?** See
> [docs/ADOPTION.md](docs/ADOPTION.md) — step-by-step porting guide, including
> how to switch to a different authorization scheme (OAuth2 client credentials,
> API key, static token, fully custom) without changing framework code.

**Testing policy:** all functional/integration tests are written in .NET
(NUnit). k6 (JavaScript) is the only exception, used exclusively for load
testing. The Python utilities in `tools/` are supporting tooling (generators,
mock server, diagnostics), not a test layer.

## Architecture

### Microservices
- **AuthService** (`:5300`) — User registration & login, JWT Bearer token generation (tokens carry a role claim)
- **ProductService** (`:5100`) — CRUD for products (requires JWT authorization; `DELETE` additionally requires the `Admin` role)
- **OrderService** (`:5200`) — CRUD for orders, depends on ProductService for product validation (requires JWT authorization)

### Test Framework
- **ApiTestFramework.Core** — Generic request builder, API client, Autofac DI, Allure integration, FluentAssertions extensions, multi-identity JWT token management
- **ApiTestFramework.Clients** — NSwag-generated DTOs and placeholder DTOs + offline swagger files
- **ApiTestFramework.Steps** — Step classes per service, generated fluent test-data builders (+ partial customizations), Bogus data generators
- **ApiTestFramework.OpenApi** — OpenAPI spec loader, schema extractor, builder & test-case scaffolders
- **ApiTestFramework.Generator.Cli** — console tool that regenerates builders and test scaffolds from swagger
- **ApiTestFramework.Tests** — NUnit test fixtures (45 tests)

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
| Load Testing | k6 (scripts generated from OpenAPI — `tools/generate_k6.py`) |
| Fuzz / Contract Testing | Schemathesis (`tools/run_schemathesis.py`) |
| Smoke Testing | OpenAPI-driven Python runner (`tools/smoke_runner.py`) |
| API Exploration | Generated Postman & Bruno collections (`tools/generate_collections.py`) |

## Authentication Flow

Authentication is **pluggable** — `RequestBuilder.Build()` consults the active
`IAuthenticationProvider` (selected by the `Authentication` section of
`appsettings.test.json`) and attaches the right header to every request.
Built-in modes:

| Mode | Use case | Code required |
|------|----------|---------------|
| `SessionToken` (default) | Token acquired once at suite start (login flow) and stored in `TokenProvider` | login call in `GlobalSetup` |
| `OAuth2ClientCredentials` | IdentityServer / Auth0 / Keycloak / Azure AD M2M — fetched & auto-refreshed | none (config only) |
| `ApiKey` | Key in a configurable header (e.g. `X-Api-Key`) | none (config only) |
| `StaticToken` | Long-lived PAT from a CI secret / env var | none (config only) |
| `None` | Anonymous APIs | none |
| `Custom` | Anything else (HMAC, cookies, ...) — implement `IAuthenticationProvider` | one small class |

Secrets are supplied via `TEST_`-prefixed environment variables
(`TEST_Authentication__ApiKey`, `TEST_Authentication__OAuth2__ClientSecret`, ...).
See [docs/ADOPTION.md](docs/ADOPTION.md) for full examples of every mode.

This demo system uses the default `SessionToken` mode — all ProductService and
OrderService endpoints require a valid JWT Bearer token:

1. **GlobalSetup** registers a default user via AuthService and stores the token in `TokenProvider`
2. **GlobalSetup** additionally registers every identity from the `Identities` section of `appsettings.test.json` (e.g. `admin` with role `Admin`) and stores each token in `TokenProvider` under its name
3. **RequestBuilder.Build()** auto-injects the default Bearer token (via the active `IAuthenticationProvider`) into every request that doesn't specify auth explicitly
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

### 6. Run everything in GitHub Actions
The [`Tests`](.github/workflows/tests.yml) workflow runs automatically on every pull request
and push to `develop`, and on demand: **Actions → Tests → Run workflow** (optionally with an
NUnit filter such as `FullyQualifiedName~OrderService`).

| Job | What runs |
|-----|-----------|
| **Build & unit tests** | `dotnet build`, `Category=Unit` tests, Python smoke runner against the in-memory mock |
| **All tests** | `docker compose up --wait` (SQL Server, Redis, 3 services) → the whole NUnit suite → Python smoke & contract checks against the live stack |

Each run publishes a summary on the run page and uploads artifacts: TRX results, raw Allure
results, a generated **Allure HTML report** (`allure-report/index.html`), smoke reports and
the service logs.

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
│   └── ApiTestFramework.Tests/         # NUnit test fixtures (Auth, Product, Order, Framework — 45 tests)
│       └── Generated/                  # *.cs.txt test scaffolds (promote to .cs by hand)
├── tools/                              # Generic OpenAPI-driven Python tooling (see tools/README.md)
│   ├── openapi_common.py               #   shared lib: service registry, spec parsing, payload generation
│   ├── generate_collections.py         #   → collections/ (Postman + Bruno, JWT pre-wired)
│   ├── generate_k6.py                  #   → perf/k6/ (load-test scripts: smoke/load/stress)
│   ├── smoke_runner.py                 #   live smoke + contract validation (stdlib only)
│   ├── run_schemathesis.py             #   property-based fuzzing wrapper
│   └── mock_server.py                  #   in-memory mock of all services from the specs
├── collections/                        # Generated Postman + Bruno collections
├── perf/k6/                            # Generated k6 load-test scripts
├── scripts/
│   ├── refresh-swagger.ps1             # pull swagger.json from running services
│   └── regenerate-all.ps1              # full regeneration pipeline
├── Jenkinsfile                         # CI/CD pipeline
└── ApiTestFramework.sln
```

## Generic OpenAPI Tooling (Python)

Everything under `tools/` is generated from the committed swagger specs —
adding a new microservice only requires one registry entry in
`tools/openapi_common.py`. See [tools/README.md](tools/README.md) for details.

```bash
python3 tools/smoke_runner.py                # smoke + contract check of live services
python3 tools/mock_server.py                 # run all services as in-memory mocks
python3 tools/generate_collections.py        # regenerate Postman/Bruno collections
python3 tools/generate_k6.py                 # regenerate k6 load-test scripts
k6 run -e PROFILE=load perf/k6/all-services.js
python3 tools/run_schemathesis.py            # fuzz every endpoint (pip install -r tools/requirements.txt)
```

## Key Framework Features

### Automatic Authentication Header Injection
The `RequestBuilder` automatically attaches the header produced by the active
`IAuthenticationProvider` (in this demo: the JWT from `TokenProvider`) to every
request. No manual token handling needed in tests:

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

### Test Lifecycle (`BaseTest`)
```csharp
public class MyTests : BaseTest
{
    private ProductServiceSteps _steps = null!;

    // Fixture setup goes into the OnFixtureSetUp hook (not a custom [OneTimeSetUp]).
    // If it throws, every test is reported as failed in Allure with the root cause —
    // a throwing [OneTimeSetUp] would make the tests disappear from the report.
    protected override void OnFixtureSetUp() => _steps = new ProductServiceSteps();

    [Test]
    public async Task Scenario()
    {
        var product = await _steps.CreateProductAsync();
        // Runs in TearDown whatever the test outcome; LIFO order, failures are logged and ignored
        RegisterCleanup(() => _steps.AsIdentity("admin").DeleteProductAsync(product.Id));
        // ...
    }
}
```

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
| OrderService | 11 | Create (single/multi-item, nested address+shipping, invalid product, empty items), get, get all, update (nested customer), delete, full lifecycle |
| Framework | 16 | Unit tests for the pluggable authentication layer (providers, factory, RequestBuilder injection, WithoutAuth/AsIdentity precedence) and BaseTest cleanup — run without live services |

## License

Licensed under the [Apache License, Version 2.0](LICENSE). See [NOTICE](NOTICE) for copyright information.

Note: `FluentAssertions` is pinned to 7.x (Apache 2.0). Versions 8.0+ use a commercial license that requires a paid license for commercial use.
