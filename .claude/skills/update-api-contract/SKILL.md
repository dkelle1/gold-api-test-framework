---
name: update-api-contract
description: Brings the committed OpenAPI contract, DTOs and all generated artifacts (builders, test scaffolds, Postman/Bruno collections, k6 scripts) in line with an API change, keeping the curated parts of the contract. Use when an endpoint, request/response schema or status code changed, when CI reports generated files are stale, or when the smoke runner reports contract mismatches.
argument-hint: "[service] [what changed]"
---

# Update an API contract

The committed swagger (`*.Clients/swagger/*.json`) is the single source for generated code. Change it
deliberately, then regenerate everything in **one commit**.

## 1. Get the new truth

- From a running service: `./examples/demo-shop/api-tests/scripts/refresh-swagger.ps1`
  (or `curl -s http://localhost:<port>/swagger/v1/swagger.json`). CI also uploads the live specs as the
  `live-swagger/` artifact of every run.
- From a smoke-runner or contract-validation failure: the message shows operation, field, expected
  and actual (e.g. `$.status: value 'Shipped' not in enum [0, 1, 2]`,
  `a response body is not documented for 400`).

## 2. Merge into the curated contract — do not overwrite blindly

Live Swashbuckle output usually **loses** information the generators rely on. Keep:

| Keep from the curated contract | Why |
|--------------------------------|-----|
| `required` arrays | builders/scaffolds and negative tests |
| 401/403 responses on secured operations | security scaffolds, smoke 401 checks |
| precise `nullable` (no blanket nullability) | contract validation |
| schema names used as DTO class names (`Product`, `Order`) | builders reference DTOs by name |

Take from the live spec: new/removed operations, new/removed properties, changed types and enum
representation, new status codes.

## 3. DTOs

Update `DemoShop.Clients/Dtos/<Service>Dtos.cs` to match (names, types, nullability, enum values).

## 4. Regenerate everything

```bash
dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config examples/demo-shop/api-tests/generator.json
python tools/generate_collections.py
python tools/generate_k6.py
dotnet build ApiTestFramework.sln
```

Then fix compile errors in **custom** code only: `Builders/Custom/*.cs`, steps, data generators,
tests. Never patch a `*.g.cs` file (blocked by hook and deny rules anyway).

## 5. Verify

```bash
git diff --stat                       # contract + DTOs + generated files + your fixes, nothing else
python tools/mock_server.py &  python tools/smoke_runner.py         # schemas still consistent
dotnet test examples/demo-shop/api-tests/DemoShop.Tests             # with the stack running
```

Review new scaffolds in `DemoShop.Tests/Generated/*.cs.txt`: a newly documented status code
usually deserves a real test (`add-api-tests`).

## Done when

- [ ] Contract, DTOs and all generated artifacts change in the same commit
- [ ] Curated information preserved (required, 401/403, nullability, schema names)
- [ ] Build green; smoke runner passes on mock (and live stack if available); affected tests green
- [ ] CI gate "Generated code is up to date" will pass (re-running the generators changes nothing)
