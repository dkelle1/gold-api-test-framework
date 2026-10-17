# API Test Framework

[![Tests](https://github.com/dkelle1/gold-api-test-framework/actions/workflows/tests.yml/badge.svg?branch=develop)](https://github.com/dkelle1/gold-api-test-framework/actions/workflows/tests.yml)

A product-agnostic .NET 8 framework for **generating and maintaining API tests from OpenAPI
contracts** — and for reusing the same building blocks in **end-to-end business flows** across
services. A complete example product (three microservices) shows every feature and runs in CI.

```
OpenAPI contract ─► DTOs + routes ─► generated test-data builders ─► steps ─► API tests + e2e flows ─► CI + Allure
        └─► generated test scaffolds · Postman/Bruno · k6 · smoke & contract checks · fuzzing · mock server
```

## Highlights

- **Generated from the contract** — fluent builders for nested request DTOs (customizable via
  partial classes) and a test checklist per documented response code; CI fails when generated
  code is stale.
- **Steps, not HTTP calls** — tests and e2e flows read as business operations; one place per
  endpoint to change.
- **Pluggable authentication** — session token, OAuth2 client credentials, API key, static token
  or custom; **multiple identities** per suite and per request (`AsIdentity("admin")`, `WithoutAuth()`).
- **Allure reporting** of every request and response, retries for transient failures, a test
  lifecycle that keeps setup failures visible.
- **One contract, many tools** — Postman/Bruno collections, k6 load scripts, a schema-validating
  smoke runner, schemathesis fuzzing and a mock server, all generated from the same specs.
- **Configuration, not forks** — services, generators and tools are configured per product; the
  framework never references a concrete service.

## Repository layout

```
framework/              the reusable framework (Core, OpenApi generators, Generator.Cli) + its unit tests
tools/                  reusable Python tooling driven by openapi-tools.json
examples/demo-shop/     the example: services under test + their API tests and e2e flows
docs/                   architecture, tutorial (A to Z), reference
```

## Quick start

Requires the .NET 8 SDK and Docker.

```bash
# 1. framework unit tests (no services needed)
dotnet test framework/tests/ApiTestFramework.Framework.Tests

# 2. start the example system under test
docker compose -f examples/demo-shop/docker-compose.yml up -d --build --wait

# 3. run the example API tests and end-to-end flows
dotnet test examples/demo-shop/api-tests/DemoShop.Tests

# 4. stop it
docker compose -f examples/demo-shop/docker-compose.yml down -v
```

The same runs on every pull request in GitHub Actions, and on demand via *Actions → Tests → Run workflow*.

## Documentation

| | |
|---|---|
| [Architecture](docs/architecture.md) | how the framework, the example and the tools fit together |
| [Tutorial: API test generation, A to Z](docs/tutorial/README.md) | ten chapters from a contract to e2e flows in CI, on the example product |
| [Adopting the framework](docs/tutorial/10-adopt-for-your-product.md) | set it up for your own services |
| [Reference](docs/README.md) | core, authentication, generators, Python tooling, CI |
| [Example system](examples/demo-shop/README.md) | the demo services and their APIs |

## Testing policy

Functional, integration and end-to-end tests are written in .NET (NUnit). k6 is used only for load
testing; the Python tools are supporting utilities (generators, smoke/contract gate, mock), not a
test layer.

## License

Licensed under the [Apache License, Version 2.0](LICENSE). See [NOTICE](NOTICE) for copyright information.

`FluentAssertions` is pinned to 7.x (Apache 2.0); versions 8.0+ require a commercial license for commercial use.
