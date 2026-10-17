# Contract validation — every response checked against OpenAPI

`framework/src/ApiTestFramework.Contracts` checks API responses against the committed OpenAPI
contracts: the **operation** must be documented, the **status code** must be documented for it, and
the **body** must match the documented schema (or be empty when no body is documented).

Typed deserialization cannot do this: a DTO silently ignores extra fields, fills in missing ones with
defaults and happily accepts an enum as a number when the contract says string. The validator does
not — and once enabled it turns **every existing API and e2e test into a contract test** without
writing a line of test code.

- [Enabling it](#enabling-it)
- [Modes](#modes)
- [What is checked](#what-is-checked)
- [Explicit assertion](#explicit-assertion)
- [Opting a request out](#opting-a-request-out)
- [Coverage report](#coverage-report)
- [Limits](#limits)

---

## Enabling it

1. Reference `ApiTestFramework.Contracts` from the test project and copy the contracts to the test
   output (the demo shop links them from the Clients project):

   ```xml
   <ProjectReference Include="..\..\..\..\framework\src\ApiTestFramework.Contracts\ApiTestFramework.Contracts.csproj" />
   <Content Include="..\DemoShop.Clients\swagger\*.json" LinkBase="swagger" CopyToOutputDirectory="PreserveNewest" />
   ```

2. Configure it in `appsettings.test.json` — one entry per ApiClient name (the `Services` key):

   ```json
   "ContractValidation": {
     "Mode": "Fail",
     "Specs": {
       "ProductService": "swagger/product-swagger.json",
       "OrderService":   "swagger/order-swagger.json"
     },
     "StrictObjects": true,
     "ReportDirectory": "contract-coverage"
   }
   ```

   Paths are relative to the test assembly directory. Responses of services without an entry are not
   checked. Override per run with `TEST_ContractValidation__Mode=Warn`.

3. Turn it on in the global setup, after the container, before the first request:

   ```csharp
   ContractValidation.EnableFromConfiguration();   // [OneTimeSetUp]
   // ...
   ContractValidation.WriteCoverageReport();        // [OneTimeTearDown]
   ContractValidation.Disable();
   ```

## Modes

| Mode | Automatic checks of every response | On violation |
|------|-----------------------------------|--------------|
| `Off` | no | — (`ShouldMatchContract()` still works) |
| `Warn` | yes | logged as a warning, attached to Allure, recorded in the coverage report |
| `Fail` | yes | the test fails **at the request** with every violation listed |

Start a new product with `Warn` to see how far contract and services are apart, fix the contracts,
then switch to `Fail`.

A failure looks like this:

```
Contract violations for GET /api/orders/{id} (/api/orders/7) → 200 [OrderService]:
  - $.status: value 'Shipped' not in enum [0, 1, 2, 3, 4]
  - $.shipping.notes: property is not declared in the schema
```

## What is checked

| Check | Rule |
|-------|------|
| Operation | method + path must exist. Requests built with `RequestBuilder` are matched by their route template (exact); other paths by the most specific template (`/api/products/category/{category}` beats `/api/products/{id}`) |
| Status code | exact code, else an `NXX` range, else `default`; otherwise "status N is not documented (documented: …)" |
| Body presence | documented schema → a JSON body is required; no documented content → the body must be empty |
| Content type | a body checked against a JSON schema must be sent as JSON |
| Types | `object`, `array`, `string`, `integer` (no fractions, `int32` range), `number`, `boolean` |
| Nullability | `null` only where `nullable: true` (also through `nullable` + `oneOf` wrappers) |
| Enums | value **and JSON kind** must match: `0` is not `"Pending"` and `"Pending"` is not `0` |
| Objects | `required` properties present; with `StrictObjects` (default) also no undeclared properties (unless `additionalProperties` is a schema) and no missing non-nullable properties |
| Composition | `allOf` (merged), `oneOf` (exactly one; `discriminator` mapping honoured), `anyOf`, `not` |
| Formats & limits | `date-time`, `date`, `uuid`, `email`, `uri`; `minLength`/`maxLength`/`pattern`; `minimum`/`maximum` (incl. exclusive); `minItems`/`maxItems` |

Error responses are part of the contract: if a service returns `{"message": "..."}` with 400, document
it (the demo shop's `ErrorResponse` schema) — otherwise the check reports
"a response body is not documented for 400".

## Explicit assertion

When automatic checks are off, or to make the intent visible in a contract-focused test:

```csharp
var response = await _products.GetProductAsync(id);
response.ShouldHaveStatusCode(HttpStatusCode.OK)
        .ShouldMatchContract();                 // contract found by request path
response.ShouldMatchContract("ProductService"); // or by name
```

Requires `ContractValidation.Enable…` (any mode) so the contracts are loaded.

## Opting a request out

For requests that deliberately provoke undocumented behaviour (e.g. probing an unspecified error):

```csharp
RequestFactory.Post("/api/orders", "not json").WithContentType("text/plain").WithoutResponseValidation();
```

Prefer documenting the behaviour in the contract; an opt-out is a statement that the behaviour is
intentionally outside it.

## Coverage report

`WriteCoverageReport()` writes `contract-coverage.md` and `.json` (default: `contract-coverage/` next
to the test assembly): every documented operation × status code with the number of calls, violations
and the first violation, plus responses with undocumented status codes. CI appends the Markdown to the
run summary and uploads it as part of the `integration-test-results` artifact. "not exercised" rows are
the list of responses no test has produced yet — a ready-made backlog for
[tutorial chapter 5](../tutorial/05-scaffold-and-promote-tests.md).

## Limits

- Provider-side conformance: it proves the service matches **its** contract, not that a change keeps
  existing consumers working — combine it with a breaking-change diff of the contract or
  consumer-driven contract tests.
- As precise as the contract: a contract with everything nullable and no `required` lists validates
  little. Curate it ([tutorial chapter 2](../tutorial/02-onboard-a-service.md)).
- Not supported: `readOnly`/`writeOnly`, `uniqueItems`, `multipleOf`, OpenAPI 3.1 keywords, XML bodies.
  Unsupported keywords are ignored (they never produce violations).
- `ContractValidation` is process-wide static state, like `TokenProvider`.

## Under the hood

`ApiClient` runs every registered `IResponseValidator` (Core extension point, `ResponseValidators`)
after each response; `ContractValidation` registers one. You can register your own validators for
other cross-cutting response checks (headers, latency budgets, …).
