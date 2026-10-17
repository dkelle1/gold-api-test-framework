# Copilot instructions

The canonical rules for AI agents in this repository are in **`AGENTS.md`** (root) and the nested
`AGENTS.md` files in `framework/`, `examples/demo-shop/` and `tools/`. Read them before changing code;
step-by-step playbooks for recurring tasks are in `.claude/skills/<name>/SKILL.md`. The essentials:

- **Layout:** `framework/` (reusable .NET 8 test framework, knows no concrete service),
  `tools/` (Python tooling driven by `openapi-tools.json`), `examples/demo-shop/` (example services
  under test + `DemoShop.Clients/Steps/Tests`), `docs/` (architecture, tutorial, reference).
- **Dependency rule:** `framework/` and `tools/` never reference the example.
- **Tests call steps, never raw HTTP** (except deliberate anonymous/malformed requests). Fixtures
  inherit `BaseTest`, set up in `OnFixtureSetUp()`, own their data (unique values) and
  `RegisterCleanup(...)` right after creating it.
- **Never edit generated files** (`*.g.cs`, `Generated/*.cs.txt`, `examples/*/collections/**`,
  `examples/*/perf/k6/**`): change the committed swagger contract and regenerate.
- FluentAssertions stays on **7.x**; no new dependencies without asking; no `Thread.Sleep`, `[Ignore]`
  or weakened assertions to get green builds; secrets only via `TEST_…` environment variables.
- Done = `dotnet build ApiTestFramework.sln` clean, `dotnet test framework/tests/ApiTestFramework.Framework.Tests`
  green, affected `examples/demo-shop/api-tests/DemoShop.Tests` fixtures green against the stack.
- Commits: conventional (`feat:`, `fix:`, `test:`, `docs:`, `ci:`…); PRs target `develop`.
