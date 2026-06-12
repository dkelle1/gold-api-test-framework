# API Test Framework

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
- **AuthService** (`:5300`) — User registration & login, JWT Bearer token generation
- **ProductService** (`:5100`) — CRUD for products (requires JWT authorization)
- **OrderService** (`:5200`) — CRUD for orders, depends on ProductService for product validation (requires JWT authorization)

### Test Framework
- **ApiTestFramework.Core** — Generic request builder, API client, Autofac DI, Allure integration, FluentAssertions extensions, JWT token management
- **ApiTestFramework.Clients** — NSwag-generated DTOs and placeholder DTOs
- **ApiTestFramework.Steps** — Step classes per service (Bogus data generators, Allure step annotations)
- **ApiTestFramework.Tests** — NUnit test fixtures (34 tests)

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

1. **GlobalSetup** registers a user via AuthService and stores the token in `TokenProvider`
2. **RequestBuilder.Build()** auto-injects the Bearer token into every request
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
│   └── ApiTestFramework.Tests/  # NUnit test fixtures (Auth, Product, Order, Framework — 34 tests)
├── tools/                       # Generic OpenAPI-driven Python tooling (see tools/README.md)
│   ├── openapi_common.py        #   shared lib: service registry, spec parsing, payload generation
│   ├── generate_collections.py  #   → collections/ (Postman + Bruno, JWT pre-wired)
│   ├── generate_k6.py           #   → perf/k6/ (load-test scripts: smoke/load/stress)
│   ├── smoke_runner.py          #   live smoke + contract validation (stdlib only)
│   ├── run_schemathesis.py      #   property-based fuzzing wrapper
│   └── mock_server.py           #   in-memory mock of all services from the specs
├── collections/                 # Generated Postman + Bruno collections
├── perf/k6/                     # Generated k6 load-test scripts
├── Jenkinsfile                  # CI/CD pipeline
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
| Framework | 12 | Unit tests for the pluggable authentication layer (providers, factory, RequestBuilder injection) — run without live services |
