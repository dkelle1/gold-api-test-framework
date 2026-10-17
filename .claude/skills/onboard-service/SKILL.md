---
name: onboard-service
description: Puts a new microservice under test - commits and reviews its OpenAPI contract, adds DTOs and routes, registers it in appsettings.test.json, generator.json and openapi-tools.json, generates builders and scaffolds, and creates its step class. Use when a new service, API or OpenAPI file has to be added to the test framework.
argument-hint: "[ServiceName] [swagger file or URL] [base URL]"
---

# Onboard a service

Inputs: service name (PascalCase, e.g. `InventoryService` — used in namespaces and file names),
its OpenAPI 3 document, local base URL. Paths use the demo shop; for another product use its folders.
Tutorial background: `docs/tutorial/02-onboard-a-service.md`.

## 1. Contract

1. Save as `examples/demo-shop/api-tests/DemoShop.Clients/swagger/<service-kebab>-swagger.json`.
2. Review and curate it (this is the source of every generated artifact):
   - `required` lists on request schemas;
   - all reachable status codes — **401 on every secured operation**, 403 where roles apply, 400/404/409;
   - `nullable` only where the API returns null; enums exactly as on the wire (integers for .NET enums
     without `JsonStringEnumConverter`);
   - schema names = the DTO class names you will create.

## 2. DTOs and routes

- `DemoShop.Clients/Dtos/<Service>Dtos.cs`, namespace `DemoShop.Clients.<Service>`, one class per
  schema (nested types included). Alternative: NSwag (`nswag-*.nswag` as template) — then do **not**
  also keep hand-written DTOs for that service.
- `DemoShop.Clients/Routes.cs`: `public static class <Service>Routes { public const string Base = "/api/…"; … }`.

## 3. Register in the three configs (same name everywhere)

| File | Entry |
|------|-------|
| `DemoShop.Tests/appsettings.test.json` | `"Services": { "<Service>": { "BaseUrl": "http://localhost:<port>" } }` |
| `api-tests/generator.json` | `{ "name": "<Service>", "swagger": "<file>.json" }` |
| `api-tests/openapi-tools.json` | `{ "key": "<short>", "name": "<Service>", "swagger": "<file>.json", "baseUrl": "…", "envVar": "<SHORT>_BASE_URL" }` — dependencies before dependants |
| `api-tests/scripts/refresh-swagger.ps1` | one line in `$services` (if the service exposes `/swagger/v1/swagger.json`) |

## 4. Generate

```bash
dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config examples/demo-shop/api-tests/generator.json
python tools/generate_collections.py && python tools/generate_k6.py
dotnet build ApiTestFramework.sln
```

Builders appear in `DemoShop.Steps/Builders/<Service>/Generated/`, scaffolds in
`DemoShop.Tests/Generated/<Service>GeneratedTests.cs.txt`. Domain defaults/verbs (e.g. a valid foreign
key) go into `DemoShop.Steps/Builders/Custom/<Schema>Builder.cs` partials.

## 5. Step class

Create `DemoShop.Steps/ServiceSteps/<Service>Steps.cs` by copying the structure of
`ProductServiceSteps` (named client via `ContainerProvider.ResolveNamed<ApiClient>("<Service>")`,
`AsIdentity`/`WithToken` copies, private `Authorized(...)`), then one step per operation.
If creating its resources needs another service, use that service's steps (see `OrderServiceSteps`).

## 6. First tests and checks

- Promote the most important scaffolds with the `add-api-tests` skill.
- Docker: add the service to `examples/demo-shop/docker-compose.yml` only if it is part of the example
  system — **ask first**.
- `python tools/smoke_runner.py --list` shows the new operations; run it against the stack or the mock.

## Done when

- [ ] Contract reviewed and committed; DTO names match schema names
- [ ] Service registered in all configs under the same name
- [ ] Generated files and Python artifacts committed with the contract
- [ ] Step class exists; at least the happy path and 401 are real tests; build green
