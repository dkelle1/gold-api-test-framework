# Adoption Guide — reusing this framework for another product

This repository is two things at once:

1. a **demo system** — three .NET 8 microservices (`src/`) used as the system under test,
2. a **reusable API test framework** (`tests/ApiTestFramework.*` + `tools/` + `perf/`)
   that is deliberately product-agnostic.

This guide explains how to take the framework to another company/product —
different services, different domain, and **different authorization**.

## Testing policy

- **All functional/integration tests are written in .NET (NUnit).**
- **k6 (JavaScript) is the only exception**, used exclusively for load/performance testing.
- The Python utilities in `tools/` are *supporting tooling* (collection/k6 generators,
  an OpenAPI mock server, a diagnostic smoke runner) — they are not a test layer and
  do not replace .NET tests.

## What is generic vs demo-specific

| Component | Status |
|-----------|--------|
| `ApiTestFramework.Core` (RequestBuilder, ApiClient, DI, assertions, Allure logging, **auth providers**) | **Fully generic — reuse as-is** |
| `ApiTestFramework.OpenApi` (spec loader, test-case scaffolder) | **Fully generic — reuse as-is** |
| `tools/` + `perf/k6` (generators driven by the swagger specs) | **Generic** — one registry entry per service |
| `ApiTestFramework.Clients` (DTOs, NSwag configs, swagger specs) | **Replace** with your product's specs/DTOs |
| `ApiTestFramework.Steps` (service steps, Bogus data generators) | **Rewrite** per service — keep the pattern |
| `ApiTestFramework.Tests` (fixtures, GlobalSetup) | **Rewrite** per service — keep the pattern |
| `src/`, `docker-compose.yml`, `Jenkinsfile` | Demo / reference — replace with your environment |

## Porting checklist

1. Copy the repo (or just `tests/`, `tools/`, `perf/`) into your new project.
2. Drop your services' OpenAPI specs into `tests/ApiTestFramework.Clients/swagger/`.
3. Generate DTOs with NSwag (`tests/ApiTestFramework.Clients/*.nswag` as templates)
   or write them by hand.
4. Register your services in `appsettings.test.json` (see below).
5. Pick an authentication mode in `appsettings.test.json` (see below) —
   write code only if your scheme is non-standard.
6. Adapt `GlobalSetup.cs` (remove the demo register/login call if your auth mode
   doesn't need it).
7. Write Steps classes + Bogus data generators per service (copy the existing
   pattern from `ApiTestFramework.Steps`).
8. Write NUnit fixtures inheriting `BaseTest`.
9. Update the `SERVICES` registry in `tools/openapi_common.py` (one entry per
   service) and regenerate Postman/Bruno collections and k6 scripts.
10. Wire CI: `dotnet test` + Allure + (optionally) k6 stage.

## 1. Registering your services

`TestConfiguration` exposes a generic, product-agnostic registry. Add services
in `appsettings.test.json` — **no framework code changes**:

```json
{
  "Services": {
    "InventoryService": { "BaseUrl": "https://inventory.staging.example.com" },
    "BillingService":   { "BaseUrl": "https://billing.staging.example.com" }
  }
}
```

Every entry gets a named `ApiClient` registered in Autofac automatically:

```csharp
var client = ContainerProvider.ResolveNamed<ApiClient>("InventoryService");
var response = await client.SendAsync<Invoice>(RequestFactory.Get("/api/invoices"));
```

Base URLs can be overridden per environment via
`appsettings.test.{Environment}.json` (selected by the `TEST_ENVIRONMENT` env
var) or via environment variables with the `TEST_` prefix, e.g.
`TEST_Services__InventoryService__BaseUrl`.

## 2. Choosing an authentication mode

The framework attaches credentials through a pluggable
`IAuthenticationProvider` (`ApiTestFramework.Core/Auth/`). The active provider
is selected by the `Authentication` section of `appsettings.test.json` and is
consulted on **every** `RequestBuilder.Build()` — tests never handle tokens
manually.

Secrets should come from environment variables (CI secret store, vault), never
from committed JSON: any setting can be supplied as
`TEST_Authentication__<Key>`.

### Mode `SessionToken` (default — token acquired once at suite start)

The original behaviour of this framework: your `GlobalSetup` performs whatever
login flow your product requires (register/login, SSO test endpoint, ...) and
stores the result in `TokenProvider`; every request then carries
`Authorization: Bearer <token>`.

```json
"Authentication": { "Mode": "SessionToken" }
```

```csharp
// GlobalSetup.cs — your product's login flow, any shape you need
var response = await authClient.SendAsync<LoginResponse>(
    RequestFactory.Post("/api/v2/sessions", new { user, password }));
TokenProvider.SetToken(response.Data!.AccessToken);
```

### Mode `OAuth2ClientCredentials` (IdentityServer / Auth0 / Keycloak / Azure AD)

Config-only — the framework fetches the token from your IdP, caches it and
refreshes it before expiry. Nothing to write, nothing to call in GlobalSetup:

```json
"Authentication": {
  "Mode": "OAuth2ClientCredentials",
  "OAuth2": {
    "TokenUrl": "https://login.example.com/connect/token",
    "ClientId": "api-tests",
    "Scope": "inventory.read inventory.write"
  }
}
```

```bash
export TEST_Authentication__OAuth2__ClientSecret="<from CI secret store>"
```

### Mode `ApiKey`

```json
"Authentication": {
  "Mode": "ApiKey",
  "HeaderName": "X-Api-Key"
}
```

```bash
export TEST_Authentication__ApiKey="<from CI secret store>"
```

### Mode `StaticToken` (long-lived PAT / token issued out-of-band)

```json
"Authentication": { "Mode": "StaticToken", "Scheme": "Bearer" }
```

```bash
export TEST_Authentication__Token="<from CI secret store>"
```

`Scheme` is configurable (`"Token"`, `""` for a raw header value, ...).

### Mode `None`

```json
"Authentication": { "Mode": "None" }
```

### Mode `Custom` — anything else (HMAC signing, cookies, rotating headers...)

Implement the one-method interface and reference it from config:

```csharp
namespace MyCompany.Tests.Auth;

public sealed class HmacAuthenticationProvider : IAuthenticationProvider
{
    public AuthenticationHeader? GetAuthenticationHeader()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Sign(timestamp, Environment.GetEnvironmentVariable("HMAC_SECRET")!);
        return new AuthenticationHeader("X-Signature", $"{timestamp}:{signature}");
    }
}
```

```json
"Authentication": {
  "Mode": "Custom",
  "CustomProviderType": "MyCompany.Tests.Auth.HmacAuthenticationProvider, MyCompany.Tests"
}
```

Or assign it programmatically in your `[SetUpFixture]` (overrides config):

```csharp
AuthenticationContext.Provider =
    new DelegateAuthenticationProvider(() => MySession.CurrentToken);
```

### Per-request overrides (negative tests etc.)

Auto-injection only happens when the test hasn't set the same header itself:

```csharp
// 401 scenario — suppress auth by setting the header explicitly
RequestBuilder.Create().WithMethod(Method.Get).WithPath("/api/products")
    .WithHeader("Authorization", "");

// different identity for one request
RequestBuilder.Create().WithMethod(Method.Get).WithPath("/api/products")
    .WithBearerToken(otherUsersToken);
```

The pluggable layer is covered by unit tests in
`tests/ApiTestFramework.Tests/Framework/AuthenticationProviderTests.cs` — they
also serve as executable documentation.

## 3. DTOs and HTTP clients (NSwag)

Copy one of the `nswag-*.nswag` configs in `ApiTestFramework.Clients`, point it
at your swagger file and run:

```bash
nswag run nswag-inventory.nswag /runtime:Net80
```

Hand-written DTOs work just as well — the framework only needs POCOs that
serialize to your API's JSON.

## 4. Steps & data generators — keep the pattern

For each service create a `<Name>Steps` class (one public method per business
operation, annotated with `[AllureStep]`) and a `<Name>DataGenerator` using
Bogus. The existing `ProductServiceSteps` / `ProductDataGenerator` pair is the
reference implementation. Tests then read as scenarios:

```csharp
var invoice = await _billingSteps.CreateInvoiceAsync();
var response = await _billingSteps.GetInvoiceAsync(invoice.Id);
response.ShouldHaveStatusCode(HttpStatusCode.OK);
response.ShouldMatchDto(invoice, "GET Invoice — full DTO comparison");
```

## 5. Scaffolding tests from OpenAPI

`ApiTestFramework.OpenApi` parses any swagger file and generates NUnit test
stubs (happy path / 401 / 404 / 400 per endpoint) so a new service starts with
full scenario coverage to fill in:

```csharp
var doc = OpenApiSpecLoader.LoadFromFile("swagger/inventory-swagger.json");
var cases = TestCaseScaffolder.GenerateTestCases(
    OpenApiSpecLoader.GetEndpoints(doc), "InventoryService");
File.WriteAllText("InventoryGeneratedTests.cs",
    TestCaseScaffolder.GenerateCSharpTestClass(cases, "MyCompany.Tests.Inventory"));
```

## 6. Supporting tooling (`tools/`, `perf/k6`)

Register your services once in `tools/openapi_common.py`:

```python
SERVICES = {
    "inventory": Service(
        key="inventory", name="InventoryService",
        swagger_file="inventory-swagger.json",
        default_base_url="http://localhost:6100", env_var="INVENTORY_BASE_URL"),
}
```

then regenerate everything:

```bash
python3 tools/generate_collections.py   # Postman + Bruno for manual exploration
python3 tools/generate_k6.py            # k6 load-test scripts → perf/k6/
```

> Note: the generated k6 `setup()` and the Python utilities assume the demo's
> register-endpoint JWT flow. With a different auth mode, pass a pre-acquired
> token to k6 instead (e.g. `k6 run -e TOKEN=$(your-token-command) ...` after
> replacing `obtainToken()` with `__ENV.TOKEN` in the generated script, or
> adapt `auth_setup_js` in `tools/generate_k6.py` once for your IdP).

## 7. CI

The Jenkinsfile in this repo shows the full pipeline shape
(build → compose up → `dotnet test` → Allure). The framework itself only
requires:

```bash
dotnet test tests/ApiTestFramework.Tests --logger trx
# Allure results are written next to the test binaries (see allureConfig.json)
```

Inject environment-specific settings via `TEST_ENVIRONMENT` +
`TEST_`-prefixed variables — no JSON edits needed per environment.
