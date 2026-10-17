# Documentation

## Start here

| If you want to… | Read |
|-----------------|------|
| understand what this repository is and how it is organized | [Architecture](architecture.md) |
| learn the whole workflow hands-on, from contract to CI | [Tutorial: API test generation, A to Z](tutorial/README.md) |
| use the framework for your own product | [Tutorial chapter 10 — Adopting the framework](tutorial/10-adopt-for-your-product.md) |
| look something up | the reference pages below |

## Tutorial (cookbook)

1. [Running the example](tutorial/01-run-the-example.md)
2. [Onboarding a service](tutorial/02-onboard-a-service.md)
3. [Generating test-data builders](tutorial/03-generate-test-data-builders.md)
4. [Writing steps](tutorial/04-write-steps.md)
5. [Scaffolding and promoting tests](tutorial/05-scaffold-and-promote-tests.md)
6. [Authentication and identities](tutorial/06-authentication-and-identities.md)
7. [End-to-end flows](tutorial/07-end-to-end-flows.md)
8. [Beyond NUnit: collections, load, smoke, fuzz, mock](tutorial/08-beyond-nunit.md)
9. [CI and reporting](tutorial/09-ci-and-reporting.md)
10. [Adopting the framework for your product](tutorial/10-adopt-for-your-product.md)

## Reference

| Part | Page | Code |
|------|------|------|
| Runtime: config, clients, requests, retries, assertions, Allure, `BaseTest` | [framework/core.md](framework/core.md) | `framework/src/ApiTestFramework.Core` |
| Authentication modes, identities, per-request auth | [framework/authentication.md](framework/authentication.md) | `framework/src/ApiTestFramework.Core/Auth` |
| Builders and test scaffolds from OpenAPI, `generator.json` | [framework/generators.md](framework/generators.md) | `framework/src/ApiTestFramework.OpenApi`, `.Generator.Cli` |
| Postman/Bruno, k6, smoke, fuzz, mock, `openapi-tools.json` | [framework/python-tooling.md](framework/python-tooling.md) | `tools/` |
| GitHub Actions and Jenkins | [ci.md](ci.md) | `.github/workflows/tests.yml`, `Jenkinsfile` |
| Framework unit tests | — | `framework/tests/ApiTestFramework.Framework.Tests` |
| The example system under test | [examples/demo-shop/README.md](../examples/demo-shop/README.md) | `examples/demo-shop` |

## Working with AI coding assistants

- [AI assistants](ai-assistants.md) — how `AGENTS.md`, `CLAUDE.md`, skills, hooks and Copilot/Gemini
  configuration fit together, and how to maintain them.
- [Advanced prompts](advanced-prompts.md) — ready-made prompts for extending the framework, adding
  coverage, hunting flaky tests and reviewing changes in this repository.
