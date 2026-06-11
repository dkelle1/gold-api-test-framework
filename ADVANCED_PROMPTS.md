# Advanced Prompts — API Test Framework (.NET)

A curated library of advanced, ready-to-use prompts for working on this codebase with Claude Code (or any capable coding agent). Copy a template, fill in the `{placeholders}`, and paste it as your task description.

**Stack context baked into these prompts:** .NET 8 Minimal APIs (AuthService :5300, ProductService :5100, OrderService :5200) · JWT Bearer auth · NUnit 4 · RestSharp · Autofac · NSwag-generated DTOs · Bogus · FluentAssertions · Allure · Docker Compose · Jenkins.

---

## How to write a good prompt for this repo

1. **Name the layer.** Be explicit about whether the change belongs in `src/*` (microservices), `tests/ApiTestFramework.Core` (framework), `tests/ApiTestFramework.Steps` (step classes), or `tests/ApiTestFramework.Tests` (fixtures) — most mistakes here are layering mistakes.
2. **Anchor to existing patterns.** "Model it on the existing ProductService steps" is worth more than a page of style guidance.
3. **Define "done".** State which projects must build and that `dotnet test` must pass; for service changes, require the Docker Compose stack to come up healthy.
4. **Remember the auth flow.** Every Product/Order endpoint requires a JWT; the framework injects it via `TokenProvider` + `RequestBuilder` — prompts touching auth must respect that chain.
5. **Plan first for non-trivial work.** Ask the agent to present a plan and wait for approval before editing.
6. **Ask for self-verification.** Require actual test output, not a claim of success.

---

## 1. Framework development (tests/ApiTestFramework.*)

### 1.1 New step class for an endpoint

```text
Act as a senior SDET working on this API test framework.

Add step coverage for the endpoint {METHOD} {service}/{path}.

Process:
1. Check whether the DTOs exist in ApiTestFramework.Clients (NSwag-generated). If the endpoint is new, regenerate via scripts/refresh-swagger.ps1 against a running service — do not hand-write DTOs that NSwag should own.
2. Add a step method to the appropriate class in ApiTestFramework.Steps, following the existing conventions exactly: Allure step annotation, Bogus-generated test data with sensible defaults and overridable parameters, request built through RequestBuilder (so the Bearer token is auto-injected).
3. Register anything new in the Autofac module the same way existing steps are registered.

Definition of done:
- Steps expose both a happy-path helper and a raw variant that returns the full response for negative testing.
- dotnet build on the whole solution passes; paste the output summary.
```

### 1.2 New test fixture

```text
Write NUnit tests in ApiTestFramework.Tests for {feature/endpoint}.

Requirements:
- Use the existing step classes — fixtures must not build requests directly or touch RestSharp.
- Cover: happy path, validation failure (400), missing/invalid JWT (401), and {domain edge case, e.g. "ordering more than available stock → 409"}.
- Assertions with FluentAssertions, asserting on status code AND response body shape — use the framework's assertion extensions where they exist.
- Test data via Bogus through the steps; no hard-coded IDs (tests must be parallel-safe and re-runnable — create your own fixtures, don't depend on seed data).
- Allure: meaningful AllureSuite/AllureFeature attributes consistent with the existing 22 tests.

Run the new tests against the Docker Compose stack and paste the results.
```

### 1.3 Framework capability (Core)

```text
Extend ApiTestFramework.Core with {capability, e.g. "automatic retry with exponential backoff for 503 responses" / "response-time assertion helper" / "correlation-id propagation"}.

Constraints:
- Must be generic — no service-specific knowledge leaks into Core.
- Must not break the token auto-injection in RequestBuilder.Build() or the Allure request/response logging.
- Wire it through Autofac the same way existing Core services are registered; make it opt-in/configurable rather than changing default behavior.

First show me the public API (interfaces/signatures) you intend to add and where it hooks into the request pipeline. After approval, implement, then prove existing tests still pass with dotnet test.
```

### 1.4 DTO refresh after contract change

```text
The {service} API contract changed: {describe change}. Refresh the generated clients:
1. Start the service, run scripts/refresh-swagger.ps1, and regenerate DTOs in ApiTestFramework.Clients via NSwag.
2. Diff the regenerated code and summarize every contract change you see (renamed/removed/retyped members) — flag anything that looks like an unintended breaking change before adapting code to it.
3. Fix all resulting compile errors in Steps and Tests, preserving test intent.
4. dotnet build && dotnet test — paste summaries.
```

---

## 2. Microservice development (src/*)

### 2.1 New endpoint

```text
Add {METHOD} /{path} to {AuthService|ProductService|OrderService}.

Requirements:
- Minimal API style, consistent with the service's existing endpoints (same validation, error shape, status-code conventions).
- JWT: {requires Bearer auth like the rest of Product/Order endpoints | public, justify why}.
- If OrderService needs product data, go through the existing ProductService HTTP call pattern and forward the caller's Bearer token (see README auth-flow diagram).
- Update the Swagger surface so NSwag regeneration picks it up.

Then add framework coverage for it: steps in ApiTestFramework.Steps + an NUnit fixture in ApiTestFramework.Tests (happy path, 400, 401, {edge case}).

Done = docker-compose up succeeds with healthy services and dotnet test is green — paste both outputs.
```

### 2.2 Cross-service bug

```text
Investigate this cross-service bug — diagnosis first, no fixes yet.

Symptom: {e.g. "OrderService returns 500 instead of 409 when the product is out of stock"}
Repro: {request sequence}

Process:
1. Reproduce against the Docker Compose stack; capture the actual request/response pair from the Allure log or service logs.
2. Trace the full path: OrderService handler → ProductService HTTP call (token forwarding) → response mapping. Identify the exact line where behavior diverges from intent.
3. Report: root cause, why the existing tests miss it, and the fix you'd make in the service vs. the regression test you'd add in the framework. Wait for my go-ahead.
```

---

## 3. Stability, CI, and infrastructure

### 3.1 Flaky test hunt

```text
The test {fixture.test name} is flaky in Jenkins.

1. Run it 10× against a fresh Docker Compose stack: dotnet test --filter "{name}" (loop it). Report the failure rate and the exact failure mode.
2. Classify: test-data collision (shared state / non-unique Bogus data), service startup race (healthcheck timing), or a real service bug. This repo has a history of stock-quantity and healthcheck-timing flakes — check git log for prior art before inventing a new theory.
3. Fix the root cause — no Thread.Sleep, no blind retries. If the fix is test data isolation, make it structural (per-test unique data via Bogus) so the whole class of flake dies.
4. Re-run 10× and paste the results.
```

### 3.2 Jenkins pipeline failure

```text
The Jenkins build failed at stage {stage}. Log excerpt:

{paste log}

Read the Jenkinsfile to see exactly what this stage runs (note: Allure results are copied from the bin output dir — ALLURE_RESULTS_DIRECTORY is not supported by Allure.NUnit 2.12.x). Classify the failure: code regression / test flake / infra (Docker, ports, healthchecks) / reporting-only. Fix accordingly; if reporting-only, the build must not be marked red for it. Explain the evidence behind your classification before changing anything.
```

### 3.3 Docker Compose health

```text
Audit docker-compose.yml and the service Dockerfiles for startup reliability. Known constraints: services must bind 0.0.0.0:8080 in containers (--urls), AuthService must be up before dependent tests run, and healthcheck timing was previously tuned to 180s start_period. Verify ordering (depends_on + healthchecks) actually guarantees readiness, propose only changes that fix a demonstrable gap, and validate by running the full stack from cold 3× with timing logs.
```

---

## 4. Review prompts

### 4.1 Pre-merge self-review

```text
Review the current branch diff against develop as a skeptical senior reviewer. For each finding give file:line, severity (blocker/major/minor), and a concrete fix. Focus on:
1. Layering violations (fixtures bypassing steps, Core knowing about specific services)
2. Test-data isolation: anything hard-coded or shared that breaks parallel runs
3. Auth: new endpoints missing JWT enforcement, or token forwarding broken in cross-service calls
4. Assertion quality: tests that pass on status code alone while the body is wrong
5. Allure: steps/attachments missing on new code paths
Finish with a verdict: merge / fix-then-merge / rethink.
```

### 4.2 Security review

```text
Security-review {scope: the diff | the three microservices}. Threat-model briefly first (assets: user credentials, JWT signing key, order/product data; trust boundary: everything behind Bearer auth except AuthService register/login). Then check: JWT validation gaps (alg/expiry/audience), the shared symmetric key handling (hard-coded? in config? rotated?), endpoints reachable without auth that shouldn't be, injection via unvalidated payloads, and token forwarding leaking credentials in logs (including Allure request/response attachments). Report only exploitable or plausibly exploitable issues, each with a proof-of-concept request and a fix.
```

---

## 5. Advanced techniques (apply to any prompt above)

| Technique | How to use it |
|---|---|
| **Plan-then-act** | "Present a plan and wait for approval before editing." The single best guard against wasted work on non-trivial tasks. |
| **Subagent fan-out** | "Use parallel subagents to audit src/, Core, Steps and Tests simultaneously, then synthesize." Ideal for cross-layer audits. |
| **Few-shot by example** | "Follow the exact pattern of the existing ProductService steps" beats paragraphs of style description. |
| **Structured output** | "Report as a table: file:line · severity · issue · fix." Forces completeness, speeds up review. |
| **Self-verification loop** | "Run dotnet test; if anything fails, fix and re-run until green, then paste the final summary." |
| **Negative constraints** | State what must NOT change: "Do not hand-edit NSwag-generated files", "no Thread.Sleep", "no new NuGet packages without justification." |
| **Scope fences** | "Touch only tests/. If the fix genuinely requires a service change, stop and tell me first." |
| **Rubber-duck mode** | "Change nothing — explain end-to-end how a test request gets its Bearer token, file by file." Use before delegating auth-adjacent work. |

---

## 6. Quick command reference

| Task | Command |
|---|---|
| Build everything | `dotnet build ApiTestFramework.sln` |
| Run all tests | `dotnet test` |
| Run one fixture | `dotnet test --filter "FullyQualifiedName~{FixtureName}"` |
| Full stack | `docker-compose up --build` |
| Run a service locally | `dotnet run --project src/{Service}` (AuthService first) |
| Refresh DTOs | `scripts/refresh-swagger.ps1` (services must be running) |
| Allure report | results land in the test bin output dir (see Jenkinsfile) |
