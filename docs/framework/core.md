# ApiTestFramework.Core — reference

`framework/src/ApiTestFramework.Core` is everything a test needs at runtime: configuration,
named HTTP clients, a fluent request builder, authentication, assertions, Allure logging and
the `BaseTest` lifecycle. It contains no knowledge of any concrete service.

- [Configuration](#configuration)
- [Clients and dependency injection](#clients-and-dependency-injection)
- [Building and sending requests](#building-and-sending-requests)
- [Timeouts and retries](#timeouts-and-retries)
- [Assertions](#assertions)
- [Allure reporting and logging](#allure-reporting-and-logging)
- [Test lifecycle — `BaseTest`](#test-lifecycle--basetest)
- [Response validators](#response-validators)
- [Waiting for eventual consistency](#waiting-for-eventual-consistency)
- [Authentication](authentication.md) and [contract validation](contracts.md) (separate pages)

---

## Configuration

`ConfigurationProvider` builds an `IConfiguration` from, in order of precedence (last wins):

1. `appsettings.test.json` next to the test assembly (**required**),
2. `appsettings.test.{TEST_ENVIRONMENT}.json` (optional; `TEST_ENVIRONMENT` defaults to `Development`),
3. environment variables prefixed with `TEST_` (`__` separates sections).

`ConfigurationProvider.GetTestConfiguration()` binds it to `TestConfiguration`:

```jsonc
{
  "Services": {                       // one named ApiClient per entry
    "OrderService": { "BaseUrl": "http://localhost:5200", "Name": "Orders" }   // Name: label in logs/Allure (default: key)
  },
  "Identities": [                     // optional; your GlobalSetup decides what to do with them
    { "Name": "admin", "Role": "Admin" }
  ],
  "Authentication": { "Mode": "SessionToken" },   // see authentication.md
  "DefaultTimeoutSeconds": 30,        // per-request timeout of every ApiClient
  "RetryCount": 0,                    // transient-failure retries (0 = off)
  "Environment": "Development"        // free-form label, logged by the example GlobalSetup
}
```

| Override | Example |
|----------|---------|
| Service URL in CI | `TEST_Services__OrderService__BaseUrl=http://order-service:8080` |
| Secret | `TEST_Authentication__OAuth2__ClientSecret=...` |
| Environment file | `TEST_ENVIRONMENT=Staging` → loads `appsettings.test.Staging.json` |

> The framework itself only reads `Services`, `Authentication`, `DefaultTimeoutSeconds` and
> `RetryCount`. `Identities` and `Environment` are conventions used by the consumer's `GlobalSetup`.

## Clients and dependency injection

`ContainerProvider` is a static facade over an Autofac container that your `[SetUpFixture]`
initializes once:

```csharp
var config = ConfigurationProvider.GetTestConfiguration();
ContainerProvider.Initialize(builder => builder.RegisterModule(new ApiClientModule(config)));
```

`ApiClientModule` registers, for every entry of `Services`, a singleton `ApiClient` named after
the entry key, plus the `TestConfiguration` itself. Resolve a client by name:

```csharp
var orders = ContainerProvider.ResolveNamed<ApiClient>("OrderService");
```

You can register your own components in the same `Initialize` callback. Dispose the container in
`[OneTimeTearDown]` with `ContainerProvider.Dispose()`.

## Building and sending requests

`ApiClient` wraps a RestSharp client bound to one base URL:

| Method | Returns |
|--------|---------|
| `SendAsync(RequestBuilder)` | `RestResponse` |
| `SendAsync<T>(RequestBuilder)` | `RestResponse<T>` (JSON deserialized into `T`) |
| `ExecuteAsync(RestRequest)` / `ExecuteAsync<T>(RestRequest)` | same, for a hand-made `RestRequest` (e.g. multipart uploads) |

Responses are returned for **every** status code — nothing throws on 4xx/5xx, so negative tests
assert the status themselves.

`RequestFactory` covers the common shapes; `RequestBuilder` gives full control:

```csharp
// shortcuts
RequestFactory.Get("/api/orders");
RequestFactory.Post("/api/orders", body);          // also Put, Patch, Delete

// full builder
RequestBuilder.Create()
    .WithMethod(Method.Get)
    .WithPath("/api/orders/customer/{email}")
    .WithPathSegment("email", "jane@example.com")   // replaces {email}
    .WithQueryParameter("page", "2")
    .WithHeader("X-Correlation-Id", "abc")
    .WithTimeout(10)                                // seconds, this request only
    .WithBody(body)                                 // serialized as JSON
    .WithContentType("application/merge-patch+json");
```

Authentication per request — `.AsIdentity("admin")`, `.WithBearerToken(raw)`, `.WithoutAuth()` —
is described in [authentication.md](authentication.md#per-request-control).

Keep route templates in the consumer project as constants (the example uses
`DemoShop.Clients.ProductServiceRoutes`), so a renamed route is a one-line change.

## Timeouts and retries

- **Timeout:** `DefaultTimeoutSeconds` applies to every client; `RequestBuilder.WithTimeout(s)`
  overrides it for one request.
- **Retries:** with `RetryCount > 0`, `ApiClient` retries transient failures with exponential
  back-off (200 ms, 400 ms, 800 ms, …). The final response is returned and attached to Allure;
  every retry is logged as a warning.

| Method | Retried on |
|--------|-----------|
| GET, HEAD, OPTIONS, PUT, DELETE (idempotent) | network errors, timeouts, 5xx |
| POST, PATCH | only when **no HTTP response** was received (e.g. connection refused) — a 5xx or timeout may mean the change was applied |
| any | 4xx is never retried |

Keep `RetryCount` at `0` for suites that assert on 5xx behaviour; raise it (e.g. `TEST_RetryCount=2`)
for e2e runs against shared, flaky environments.

## Assertions

Extension methods on RestSharp responses (`ApiTestFramework.Core.Assertions`). On failure they
attach a readable summary of the response to Allure before throwing.

| Method | Checks |
|--------|--------|
| `ShouldHaveStatusCode(HttpStatusCode)` | exact status (generic and non-generic responses) |
| `ShouldBeSuccessful()` | 2xx |
| `ShouldHaveData()` | body deserialized to non-null `T`; **returns `T`** |
| `ShouldHaveContent()` | non-empty raw body |
| `ShouldMatchDto(expected, label, config?)` | deep structural equality (FluentAssertions `BeEquivalentTo`), both sides attached to Allure |
| `ShouldMatchDtoExcluding(expected, params paths)` | same, ignoring server-generated members such as `"Id"` or `"Audit.CreatedAt"` |

```csharp
var response = await steps.GetProductAsync(id);
var product = response.ShouldHaveStatusCode(HttpStatusCode.OK).ShouldHaveData();
product.Price.Amount.Should().Be(19.99m);
```

## Allure reporting and logging

Every `ApiClient` call attaches the full request (method, URL, headers, query, body as JSON) and the full
response (status, headers, pretty-printed body) to the current Allure step. Steps marked with
`[AllureStep("...")]` become nested steps in the report. `AllureRequestLogger` also exposes
`AttachJson`, `AttachDtoComparison` and `AttachFailureSummary` for custom attachments.

> Request attachments contain the **full `Authorization` header**. Treat Allure reports from
> runs with real credentials as secrets.

`Allure.NUnit` 2.12 writes results next to the test assembly
(`bin/<Configuration>/net8.0/allure-results`); the CI pipelines copy them to the workspace —
see [CI](../ci.md). Locally: `allure serve <that folder>`, or
`allure generate <that folder> -o allure-report --single-file` for one self-contained HTML file.

Logging uses Serilog's static `Log`. Configure `Log.Logger` in your `GlobalSetup`; `BaseTest`
logs fixture and test start/finish with the test name.

## Test lifecycle — `BaseTest`

Inherit every fixture from `BaseTest` (it also applies `[AllureNUnit]`).

```csharp
public class OrderTests : BaseTest
{
    private OrderServiceSteps _orders = null!;

    // Fixture setup goes here — not into your own [OneTimeSetUp]
    protected override void OnFixtureSetUp() => _orders = new OrderServiceSteps();

    [Test]
    public async Task Something()
    {
        var order = await _orders.CreateOrderAsync(request);
        RegisterCleanup(() => _orders.DeleteOrderAsync(order.Id));   // runs in TearDown
        // ...
    }
}
```

| Hook | Behaviour |
|------|-----------|
| `OnFixtureSetUp()` | Called from BaseTest's `[OneTimeSetUp]`. If it throws, every test of the fixture is reported as **broken** in Allure with the root cause, and test bodies do not run. (A throwing plain `[OneTimeSetUp]` makes the tests disappear from the Allure report.) |
| `SetUp()` / `TearDown()` | Virtual; call `base.` when overriding. |
| `RegisterCleanup(Func<Task>)` | Per-test cleanup executed in `TearDown` whatever the outcome; **LIFO** order; a failing action is logged and does not stop the others. |
| `OneTimeTearDown()` | Virtual; logs fixture end. |
| `Logger` | Serilog logger scoped to the fixture type. |

Fixtures run sequentially by default. Global state (`TokenProvider`, `AuthenticationContext`,
`ContainerProvider`) is static, so enable NUnit parallelism only for fixtures that do not change it.

## Response validators

Every `ApiClient` response is passed to the validators registered in `ResponseValidators`
(`IResponseValidator`) after Allure logging. A validator that throws fails the test at that request.
[Contract validation](contracts.md) is one; register your own for other cross-cutting checks:

```csharp
public sealed class NoServerErrors : IResponseValidator
{
    public void Validate(ResponseValidationContext context)
    {
        if ((int)context.Response.StatusCode >= 500)
            throw new AssertionException($"{context.ServiceName} {context.Request.Method} {context.PathTemplate}: {(int)context.Response.StatusCode}");
    }
}

ResponseValidators.Register(new NoServerErrors());   // in the global setup
```

The context carries the service name, the request, the route template (for requests built with
`RequestBuilder`) and the response. `RequestBuilder.WithoutResponseValidation()` skips all validators
for one request.

## Waiting for eventual consistency

`Eventually` (`ApiTestFramework.Core.Waiting`) replaces `Thread.Sleep` when an effect appears
asynchronously:

| Method | Behaviour |
|--------|-----------|
| `UntilAsync(probe, condition, expectation, timeout?, pollInterval?)` | calls `probe` until `condition` holds and returns that value; probe exceptions count as "not yet" |
| `SatisfiesAsync(assertion, expectation, timeout?, pollInterval?)` | re-runs an assertion block (use FluentAssertions inside) until it passes |

Defaults: 10 s budget, 250 ms interval (`Eventually.DefaultTimeout`, `DefaultPollInterval`). A timeout
fails with an `AssertionException` naming the expectation, the attempts and the last observed value.

## See also

- [Contract validation](contracts.md) — every response checked against OpenAPI
- [Authentication](authentication.md) — providers, identities, per-request control
- [Generators](generators.md) — builders and test scaffolds from OpenAPI
- [Tutorial](../tutorial/README.md) — all of this used end to end
