# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versions map to merged PRs on the `main` branch.

---

## [Unreleased]

> Changes on open branches not yet merged to `main`.

### feat/allure-onetimesetup-guard (current PR)

#### Added
- Added framework self-tests fixture `TokenIsolationFrameworkTests` under `tests/ApiTestFramework.Tests/Framework`.
- Added Jenkins PR runbook for REST API job create/trigger flow in `scripts/jenkins-pr-job-runbook.md`.
- Added repository workflow skill in `.github/skills/pr-jenkins-workflow/SKILL.md`.

#### Changed
- Extracted token-isolation framework tests from ProductService test suite into a dedicated framework suite.
- Updated Copilot repository instructions to reference the Jenkins PR runbook and required delivery flow.

#### Removed
- Removed `MultiUserProductTests` from `tests/ApiTestFramework.Tests/ProductService` after extraction.
- Removed temporary repository artifact `jenkins-pr8-config.xml` (Jenkins job config export).

### feat/openapi-steps-generator (PR #5)
- **Added** `StepsGenerator` in `ApiTestFramework.OpenApi` — generates a full `*ServiceSteps.cs` from parsed `EndpointDefinition` objects.
- **Added** `ApiTestFramework.OpenApi.Cli` project (`generate-steps.exe`) — .NET 8 console app wrapping the generator with a CLI interface (`--swagger`, `--service`, `--dto`, `--ns`, `--dto-ns`, `--out`).
- **Added** `generate-steps.bat` — convenience script that regenerates all three service step files from the offline swagger specs in one command.
- **Added** `tests/ApiTestFramework.Steps/ServiceSteps/Generated/ProductServiceSteps.g.cs` — sample generated output for ProductService (6 endpoints).

### feat/multi-user-token-scoping (PR #4)
- **Added** `TokenScope` (`ApiTestFramework.Core/Auth/TokenScope.cs`) — synchronous, disposable scope that sets/restores `TestTokenContext`.
- **Added** `TestTokenContext` (`ApiTestFramework.Core/Auth/TestTokenContext.cs`) — `AsyncLocal<string?>` holder for per-test Bearer tokens.
- **Added** `UserScope` (`ApiTestFramework.Steps/Auth/UserScope.cs`) — registers a fresh user and enters a `TokenScope` for the lifetime of the using block.
- **Added** `AuthServiceSteps.RegisterUserAsync(string role)` — overload that accepts an explicit role.
- **Added** `MultiUserProductTests` (6 tests) — covers `UserScope`, `UseToken`, scope-restore, role-specific registration, and parallel isolation.
- **Modified** `ApiClient.InjectBearerToken` — priority chain: explicit `Authorization` header → `TestTokenContext` → global `TokenProvider`.
- **Fixed** silent `AsyncLocal` scoping bug: removed `UserScope.CreateAsync` (called `TokenScope.Use()` in a child async context where writes aren't visible to the caller); callers now use the synchronous two-step pattern.

---

## [3.0.0] — 2026-04-29 · feat/consul-config (PR #3)

### Added
- **HashiCorp Consul** as an optional, higher-priority configuration source for service base-URLs.
  - `consul` service in `docker-compose.yml` (hashicorp/consul:1.18, dev mode + UI on port 8500).
  - `consul-init` sidecar — seeds KV tree under `api-test-framework/` on startup.
  - KV keys: `Services/AuthService/BaseUrl`, `Services/ProductService/BaseUrl`, `Services/OrderService/BaseUrl`, `DefaultTimeoutSeconds`, `RetryCount`.
- `Winton.Extensions.Configuration.Consul 3.4.0` package dependency in `ApiTestFramework.Core`.
- `ConsulConfig` class and `TestConfiguration.Consul` property.
- Two-phase configuration build in `ConfigurationProvider`:
  - Phase 1 — JSON + env-vars (to discover Consul address).
  - Phase 2 — rebuild with `AddConsul()` layered on top when address is non-empty.
- `appsettings.test.json` — new `Consul` section (`Address`, `KeyPrefix`).
- `Jenkinsfile` — `TEST_Consul__Address=http://consul:8500` environment variable injected into the test step.

### Changed
- All three microservices in `docker-compose.yml` now declare `consul: service_healthy` as a dependency — they start after Consul is ready.
- Consul values **override** JSON; JSON remains as a fallback for local dev runs without Docker.

---

## [2.0.0] — 2026-04-28 · feat/nswag-dto-generation (PR #2)

### Added
- NSwag-generated DTOs for ProductService and OrderService (`ApiTestFramework.Clients/Dtos/`).
- Offline swagger specs: `product-swagger.json`, `order-swagger.json`, `auth-swagger.json`.
- NSwag config files: `nswag-product.nswag`, `nswag-order.nswag`.
- `generate-clients.bat` convenience script.
- **Nested DTO pattern** for all API responses:
  - `ProductResponse` → `PriceInfo`, `InventoryInfo`, `AuditInfo`
  - `OrderResponse` → `ProductRef`, `CustomerInfo`, `AuditInfo`
  - `AuthResponse` → `TokenInfo`, `UserProfile`

### Changed
- All test assertions updated to use nested DTO field access (e.g. `product.Price.Amount`, `order.Customer.Email`).

---

## [1.0.0] — 2026-04-27 · Initial release (PR #1)

### Added
- Three .NET 8 microservices: `AuthService` (`:5300`), `ProductService` (`:5100`), `OrderService` (`:5200`).
- SQL Server + Redis infrastructure via `docker-compose.yml`.
- `ApiTestFramework.Core` — `RequestBuilder`, `ApiClient`, Autofac DI (`ContainerProvider`), `TokenProvider`, `ConfigurationProvider`, `ResponseAssertions`, Allure logging.
- `ApiTestFramework.Steps` — `AuthServiceSteps`, `ProductServiceSteps`, `OrderServiceSteps`; Bogus data generators.
- `ApiTestFramework.Tests` — 22 NUnit tests across `AuthCrudTests`, `ProductCrudTests`, `OrderCrudTests`; `GlobalSetup` for shared token acquisition.
- `ApiTestFramework.OpenApi` — `OpenApiSpecLoader`, `EndpointDefinition` model, `TestCaseScaffolder` (stub generator).
- `Jenkinsfile` declarative pipeline (checkout → build → docker-compose up → test → publish → teardown).
- `COMPOSE_PROJECT_NAME=api-test-${BUILD_NUMBER}` for parallel-safe CI builds.

