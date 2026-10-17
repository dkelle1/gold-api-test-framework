---
name: extend-framework
description: Changes the reusable framework (framework/ or tools/) safely - keeps it product-agnostic, designs the public API first, adds offline unit tests, checks generator determinism and consumer impact, and updates the reference docs. Use for any change to ApiTestFramework.Core, ApiTestFramework.OpenApi, ApiTestFramework.Generator.Cli or the Python tools, including bug fixes.
argument-hint: "[capability or bug]"
---

# Extend or fix the framework

`framework/` and `tools/` are libraries for any product. Read `framework/AGENTS.md` (or
`tools/AGENTS.md`) before editing.

## 1. Design before code

- Write down the public surface you will add/change (signatures, config keys, CLI flags) and where it
  hooks in (request pipeline: `RequestBuilder.Build()` → `ApiClient.ExecuteAsync` → Allure logging).
- **Product-agnostic check:** would this make sense for an inventory API that has never heard of the
  demo shop? Anything product-specific becomes a parameter, config field or extension point.
- Prefer **additive and opt-in** (new method, new config key with a backward-compatible default).
  Breaking public API, `generator.json` or `openapi-tools.json` schema → **ask first**.

## 2. Test first, offline

Add or extend a test in `framework/tests/ApiTestFramework.Framework.Tests`:

| Area | Pattern to copy |
|------|-----------------|
| request building | `RequestBuilderTests` (inspect the built `RestRequest`) |
| HTTP behaviour (retries, timeouts) | `ApiClientRetryTests` (in-process `HttpListener` / `TcpListener` stubs) |
| auth providers / precedence | `AuthenticationProviderTests` (restore `AuthenticationContext` / `TokenProvider` afterwards) |
| test lifecycle | `BaseTestCleanupTests` |

For bugs, the test must fail before the fix. Python tools: verify with the mock server
(`python tools/mock_server.py & python tools/smoke_runner.py`) and a focused script.

## 3. Implement

- Match the surrounding style; XML doc comments on public members.
- Generators must stay deterministic (no timestamps/random/machine paths in builder output).
- Static state must be restorable; don't introduce new global singletons without a reset.

## 4. Verify consumer impact

```bash
dotnet build ApiTestFramework.sln                                   # consumers still compile
dotnet test framework/tests/ApiTestFramework.Framework.Tests
dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config examples/demo-shop/api-tests/generator.json
python tools/generate_collections.py && python tools/generate_k6.py
git diff --stat -- examples/                                        # generated output unchanged unless intended
```

If generated output changes on purpose, commit it with the change and explain why in the PR.
Run the example suite against the stack when the runtime behaviour changed.

## 5. Document

Update the reference page in the same PR: `docs/framework/core.md`, `authentication.md`,
`generators.md` or `python-tooling.md`; tutorial chapters if a workflow changed; "Breaking changes"
section in the PR description for any incompatible change.

## Done when

- [ ] No product knowledge in `framework/` / `tools/`
- [ ] Offline test covers the behaviour (fails without the change)
- [ ] Whole solution builds; framework tests green; generated output intentional
- [ ] Docs updated; breaking changes called out
