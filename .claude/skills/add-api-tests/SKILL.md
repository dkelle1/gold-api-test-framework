---
name: add-api-tests
description: Adds or extends NUnit API tests for an endpoint of a service under test - checks the OpenAPI contract, DTOs, route and step, promotes the generated scaffold into a real fixture with isolated data and cleanup, then runs it. Use when asked to test, cover or add tests for an API endpoint, status code or validation rule.
argument-hint: "[METHOD /path] [service]"
---

# Add API tests for an endpoint

Goal: real tests for `$ARGUMENTS` that go through steps, own their data and pass against the stack.
Paths below use the demo shop (`examples/demo-shop/api-tests`); for another product use its
`<Product>.Clients/Steps/Tests` projects.

## 1. Check the contract first

- Find the operation in `DemoShop.Clients/swagger/<service>-swagger.json`.
- Every status code you want to test must be **documented** (incl. 401 on secured ops, 403 for roles,
  400 for validation). Missing or wrong → fix the contract first with the `update-api-contract` skill.

## 2. Make sure the building blocks exist

| Needed | Where | If missing |
|--------|-------|-----------|
| DTOs for request/response | `DemoShop.Clients/Dtos/<Service>Dtos.cs` | add, class names = schema names |
| Route constant | `DemoShop.Clients/Routes.cs` | add to `<Service>Routes` |
| Builder for the request body | `DemoShop.Steps/Builders/<Service>/Generated/` | regenerate (never hand-write `*.g.cs`) |
| Step method | `DemoShop.Steps/ServiceSteps/<Service>Steps.cs` | add — see [templates.md](templates.md#step-method) |

Step rules: `[AllureStep("…")]`, request built with `RequestBuilder`/`RequestFactory` and passed
through `Authorized(...)`; reads return `RestResponse<T>`; creates have a happy-path variant
(asserts, returns entity) and a `Try…` variant (raw response).

## 3. Start from the scaffold

Regenerate and open `DemoShop.Tests/Generated/<Service>GeneratedTests.cs.txt`; find the scaffolds for
the operation (one per documented status code). **Do not edit the `.cs.txt`** — copy what you need.

## 4. Write the fixture

Create or extend `DemoShop.Tests/<Service>/<Topic>Tests.cs` — see [templates.md](templates.md#fixture).

- Inherit `BaseTest`; set up steps in `OnFixtureSetUp()`; `[AllureSuite]` + `[AllureFeature]`.
- Keep the scaffold's test name where it fits (`Operation_Scenario_ExpectedResult`).
- Arrange with builders / data generators and **unique values** (`$"x-{Guid.NewGuid():N}"`).
- `RegisterCleanup(...)` immediately after each create, via an actor allowed to delete
  (demo shop: products are deleted as `TestIdentities.Admin`).
- Assert status **and** business meaning (`ShouldHaveData()`, then FluentAssertions); for forbidden
  operations assert that nothing changed. Schema conformance of every response is checked
  automatically (contract validation, Fail mode) — don't re-assert field types by hand.
- 401: `RequestBuilder...WithoutAuth()`; 403: `steps.AsIdentity(TestIdentities.User)`.
- Services with an `AuthClient` (demo: ProductService = `Catalog`, OrderService = `Orders`) send that
  OAuth2 client's token by default; another group's token via `steps.AsClient(AuthClients.X)`.
- Assertions that hold on a shared database ("contains mine", "only matching") — no global counts.

## 5. Run and report

```bash
dotnet build ApiTestFramework.sln
docker compose -f examples/demo-shop/docker-compose.yml up -d --build --wait
dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~<Fixture>"
```

Paste the summary. If a test fails, decide with `diagnose-test-failure` whether the product, the test
or the contract is wrong — never weaken the assertion just to pass.

## Done when

- [ ] Contract documents every tested status code
- [ ] Steps cover the operation; no HTTP code in the test except deliberate anonymous/malformed calls
- [ ] Tests own their data and clean up; they pass twice in a row against the same database
- [ ] No generated file edited by hand; no `WithoutResponseValidation()` added to get green
- [ ] The coverage report (`contract-coverage.md`) shows the new responses as exercised
