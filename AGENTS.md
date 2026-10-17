# AGENTS.md

Instructions for AI coding agents (Claude Code, Codex, Copilot, Cursor, Gemini, …) working in this
repository. Humans: see `README.md` and `docs/`. Folder-specific rules live in nested `AGENTS.md`
files (`framework/`, `examples/demo-shop/`, `tools/`) — read the one closest to the file you change.

## What this repository is

A product-agnostic .NET 8 framework for generating API tests from OpenAPI contracts and reusing them
in end-to-end flows, plus an example product (`examples/demo-shop`) that uses it and runs in CI.

```
framework/src/        ApiTestFramework.Core · .OpenApi · .Generator.Cli   (reusable, knows no service)
framework/tests/      ApiTestFramework.Framework.Tests                    (offline unit tests)
tools/                Python tooling, configured by openapi-tools.json
examples/demo-shop/   services/ (system under test) · api-tests/ (DemoShop.Clients/Steps/Tests)
docs/                 architecture, tutorial, reference
.claude/skills/       step-by-step playbooks for recurring tasks (any agent may follow them)
```

## Commands

```bash
dotnet build ApiTestFramework.sln                                     # build everything
dotnet test framework/tests/ApiTestFramework.Framework.Tests          # framework unit tests (no services)
docker compose -f examples/demo-shop/docker-compose.yml up -d --build --wait   # start example stack
dotnet test examples/demo-shop/api-tests/DemoShop.Tests               # example API + e2e tests (needs stack)
dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~EndToEnd"
docker compose -f examples/demo-shop/docker-compose.yml down -v       # stop stack

# regenerate code from the committed contracts (builders, test scaffolds)
dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config examples/demo-shop/api-tests/generator.json
python tools/generate_collections.py && python tools/generate_k6.py   # Postman/Bruno + k6
python tools/smoke_runner.py --role Admin                             # contract smoke check (needs stack or mock)
python tools/mock_server.py                                           # in-memory mock of all services
```

Windows PowerShell wrapper: `./examples/demo-shop/api-tests/scripts/regenerate-all.ps1`.
No Docker? Run framework tests, the generators and the mock + smoke runner; say that the example
suite was not run.

## Architecture rules

1. **Dependency rule:** `framework/`, `tools/` and `scripts/ci/` never reference the example — no
   service names, routes, ports or `DemoShop` types. Product-specific things are configuration
   (`appsettings.test.json`, `generator.json`, `openapi-tools.json`) or consumer code.
2. **Tests call steps, not HTTP.** A step class per service wraps every endpoint; tests and e2e flows
   compose steps. Raw `RequestBuilder` in a test only for deliberately anonymous/malformed requests.
3. **The committed swagger is the contract** (`*.Clients/swagger/*.json`). Change the contract first,
   then DTOs, then regenerate. Live swagger is less precise — never overwrite curated parts blindly.
4. **Generated files are never edited by hand:** `*.g.cs`, `**/Generated/*.cs.txt`,
   `examples/demo-shop/collections/**`, `examples/demo-shop/perf/k6/**`. Customize builders in
   `Builders/Custom/*.cs` partial classes. CI fails when generated files are stale.

## Conventions

- C# 12 / .NET 8, nullable enabled, file-scoped namespaces, `async Task` everywhere.
- Tests: inherit `BaseTest`; fixture setup in `OnFixtureSetUp()` (never a custom `[OneTimeSetUp]`);
  name `Operation_Scenario_ExpectedResult`; `[AllureSuite]`/`[AllureFeature]` on fixtures.
- Assertions: `ShouldHaveStatusCode`, `ShouldHaveData()`, `ShouldMatchDto…` + FluentAssertions **7.x**
  (8.x is commercial — never upgrade).
- Test data: generated builders / data generators; unique values per test (GUID fragments); register
  cleanup with `RegisterCleanup(...)` right after creating data. The database is shared, never reset.
- Steps: `[AllureStep("…")]` on every public method; happy-path methods assert success and return the
  entity, `Try…` methods return the raw response.
- Python (`tools/`): standard library only, Python 3.10+, config-driven, ASCII-safe console output.
- PowerShell scripts: ASCII only (Windows PowerShell 5.1 reads UTF-8 without BOM as ANSI).

## Definition of done

- `dotnet build ApiTestFramework.sln` has 0 errors and no new warnings.
- Framework change → `ApiTestFramework.Framework.Tests` green, new behaviour covered by an offline test.
- Example/test change → the affected `DemoShop.Tests` fixtures green against the stack (or state that
  the stack was unavailable).
- Contract change → generators re-run and their output committed in the same commit.
- Docs updated when behaviour, commands or paths change (`docs/`, nested `AGENTS.md`).
- Report results honestly: paste the test summary; say what was not run.

## Boundaries

**Always**
- Read the nested `AGENTS.md` and the matching skill in `.claude/skills/` before starting a task it covers.
- Keep framework code product-agnostic; add a framework unit test for every framework behaviour change.
- Keep tests independent: own data, cleanup, no ordering assumptions between fixtures.

**Ask first**
- Adding or upgrading NuGet/Python dependencies; changing target frameworks.
- Breaking public framework API (`ApiTestFramework.Core`, `.OpenApi`, `generator.json`/`openapi-tools.json` schema).
- Changing the CI workflow, `Jenkinsfile`, `docker-compose.yml` or service behaviour in `examples/demo-shop/services`.
- Deleting or skipping existing tests; changing assertions to make a failing test pass.

**Never**
- Edit generated files (see rule 4) — regenerate instead.
- Commit secrets, tokens or real credentials; put secrets in `appsettings*.json` (use `TEST_…` env vars).
- Add `Thread.Sleep`/fixed delays or blind retries to make tests pass; mark tests `[Ignore]` to get green CI.
- Reference `examples/` from `framework/` or `tools/`.
- Push to `develop` or `main` directly, force-push shared branches, or rewrite published history.

## Git and pull requests

- Branch from `develop`; PRs target `develop`. Conventional commits: `feat:`, `fix:`, `test:`, `docs:`,
  `refactor:`, `ci:`, `chore:` with a scope where useful (`fix(core): …`).
- PR description: Summary · Changes · Validation (commands + results) · Risks / breaking changes.
- The `Tests` workflow (`.github/workflows/tests.yml`) must be green before merge.

## Where to look

- Architecture and dependency rule: `docs/architecture.md`
- Runtime API (config, requests, retries, assertions, `BaseTest`): `docs/framework/core.md`
- Authentication and identities: `docs/framework/authentication.md`
- Generators (`generator.json`, builders, scaffolds): `docs/framework/generators.md`
- Python tools (`openapi-tools.json`): `docs/framework/python-tooling.md`
- CI: `docs/ci.md` · Step-by-step tutorial: `docs/tutorial/README.md`

## Playbooks (`.claude/skills/<name>/SKILL.md`)

| Task | Skill |
|------|-------|
| Add or extend API tests for an endpoint | `add-api-tests` |
| Put a new service under test | `onboard-service` |
| An API changed — update contract, DTOs, generated code | `update-api-contract` |
| Write an end-to-end business flow | `write-e2e-flow` |
| A test fails locally or in CI | `diagnose-test-failure` |
| Change the framework itself (`framework/`, `tools/`) | `extend-framework` |
