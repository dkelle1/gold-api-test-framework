# AGENTS.md — framework/

The reusable framework. It is consumed by any product's test projects, so every change here is a
library change. Read the root `AGENTS.md` first; these rules add to it.

## Layout

| Project | Responsibility |
|---------|----------------|
| `src/ApiTestFramework.Core` | config (`ConfigurationProvider`, `TestConfiguration`), DI (`ContainerProvider`, `ApiClientModule`), HTTP (`ApiClient`, `RequestBuilder`, `RequestFactory`), auth (`TokenProvider`, `IAuthenticationProvider`, `AuthenticationContext`), assertions, Allure logging, `BaseTest` |
| `src/ApiTestFramework.OpenApi` | `OpenApiSpecLoader`, `SchemaExtractor`, `BuilderScaffolder`, `TestCaseScaffolder` — pure functions from OpenAPI to C# source |
| `src/ApiTestFramework.Generator.Cli` | console front-end; reads a `generator.json` (`GeneratorConfig.cs`) |
| `tests/ApiTestFramework.Framework.Tests` | offline NUnit tests of all of the above |

## Rules

- **Product-agnostic.** No service names, routes, ports, DTO types or `DemoShop` references —
  not in code, defaults, comments or generated headers. Anything product-specific becomes a
  parameter, a config field or an extension point.
- **Every behaviour change gets an offline unit test** in `tests/ApiTestFramework.Framework.Tests`
  (no network except in-process stubs such as `HttpListener`/`TcpListener`, see `ApiClientRetryTests`).
- **Generators stay deterministic** — no timestamps, random values or machine paths in builder
  output (scaffolds keep their single `// Generated:` line). CI diffs generated output against the commit.
- **Static state** (`TokenProvider`, `AuthenticationContext`, `ContainerProvider`) is shared by all
  tests in a run: tests that change it must restore it and must not run in parallel.
- **Public API is a contract** for consumer projects. Additive changes are fine; renames, removals
  or changed defaults need an entry under "Breaking changes" in the PR and doc updates.
- Keep package versions aligned across projects (NUnit 4.2.2, RestSharp 112.1.0, Allure.NUnit 2.12.1,
  FluentAssertions 7.0.0 — never 8.x).

## Verify

```bash
dotnet build ApiTestFramework.sln
dotnet test framework/tests/ApiTestFramework.Framework.Tests
# generator changes: output must still compile and match the commit
dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config examples/demo-shop/api-tests/generator.json
git diff --stat -- examples/
```

## Document

Behaviour or API changes update `docs/framework/core.md`, `authentication.md` or `generators.md`
in the same PR.
