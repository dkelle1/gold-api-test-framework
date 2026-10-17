# Chapter 1 — Running the example

Before writing anything, run what already exists: three services, their contracts, an API suite
with end-to-end flows, and the reporting around them.

In this chapter:

- [Starting the demo stack](#starting-the-demo-stack)
- [Running the test suites](#running-the-test-suites)
- [Reading the Allure report](#reading-the-allure-report)

---

## Starting the demo stack

### Getting ready

Install the .NET 8 SDK and Docker, clone the repository and open a terminal in its root.

### How to do it…

1. Build and start SQL Server, Redis and the three services, waiting until they are healthy:

   ```bash
   docker compose -f examples/demo-shop/docker-compose.yml up -d --build --wait
   ```

2. Open the Swagger UIs: <http://localhost:5300> (auth), <http://localhost:5100> (products),
   <http://localhost:5200> (orders).

3. When you are done (later): `docker compose -f examples/demo-shop/docker-compose.yml down -v`.

### How it works…

The compose file builds each service from `examples/demo-shop/services/<Service>/Dockerfile` and maps
it to a fixed localhost port. These ports are exactly the `BaseUrl`s in the example's
`appsettings.test.json`, so tests need no configuration to find them.

### There's more…

The services are an **example system under test** — see
[`examples/demo-shop/README.md`](../../examples/demo-shop/README.md) for every endpoint, its status
codes, and the deliberate simplifications (for instance, anyone may register as `Admin`).

---

## Running the test suites

### Getting ready

The stack from the previous recipe is running.

### How to do it…

1. Run the framework's own unit tests (no services needed):

   ```bash
   dotnet test framework/tests/ApiTestFramework.Framework.Tests
   ```

2. Run the example API and end-to-end suite:

   ```bash
   dotnet test examples/demo-shop/api-tests/DemoShop.Tests
   ```

3. Run a subset with an NUnit filter:

   ```bash
   dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~EndToEnd"
   dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~ProductService"
   ```

### How it works…

`DemoShop.Tests/GlobalSetup.cs` runs once before any test: it loads configuration, registers one
named `ApiClient` per service, registers a default user plus the `admin` and `user` identities,
and stores their tokens. Every fixture then works through **steps**
(`DemoShop.Steps/ServiceSteps`) — tests never build HTTP calls themselves.

| Folder in `DemoShop.Tests` | What it shows |
|----------------------------|---------------|
| `AuthService/` | registration, login, duplicates, wrong password, 401 |
| `ProductService/` | CRUD, browsing by category, role-based authorization |
| `OrderService/` | nested multi-item orders, partial updates, cross-service validation |
| `EndToEnd/` | customer journeys across all services and several actors |
| `Generated/` | scaffolds produced from the contracts (not compiled) |

> **Tip:** `dotnet test` at the repository root runs every test project in `ApiTestFramework.sln`,
> including the example suite — start the stack first.

---

## Reading the Allure report

### Getting ready

Install the Allure CLI (`scoop install allure`, `brew install allure`, or the zip from the Allure
releases page) and run the example suite once.

### How to do it…

```bash
allure serve examples/demo-shop/api-tests/DemoShop.Tests/bin/Debug/net8.0/allure-results
```

### How it works…

Each `[AllureStep]` method of a step class becomes a step in the report, and every HTTP call made
inside it is attached as two files: the full request and the full response. A failing assertion
attaches a summary of the offending response. Suites and features come from `[AllureSuite]` and
`[AllureFeature]` on the fixtures.

### There's more…

- In CI you do not need the CLI: every run uploads a ready-made HTML report as an artifact
  ([chapter 9](09-ci-and-reporting.md)).
- Request attachments include the `Authorization` header. Do not share reports produced with real
  credentials.

## See also

- [Architecture](../architecture.md) — how the pieces fit together
- [Core reference](../framework/core.md#allure-reporting-and-logging)
