# API test generation, A to Z — a cookbook

This tutorial takes you from an OpenAPI contract to a maintained suite of API tests and
end-to-end business flows running in CI. Every recipe is built on the **demo shop** in
[`examples/demo-shop`](../../examples/demo-shop/README.md), and every code sample is taken from
code that runs in this repository's CI — so you can read the finished result next to each step.

## Who this is for

- **QA / SDET engineers** who want generated, maintainable API tests instead of hand-written
  HTTP calls.
- **Teams adopting the framework** for their own services — chapter 10 is the checklist.
- **Developers** who need e2e checks of business flows that span several services.

You should be comfortable with C#, `async`/`await` and NUnit basics, and know what an OpenAPI
(Swagger) document is.

## What you will build

```
OpenAPI contract ──► DTOs + routes ──► generated builders ──► steps ──► API tests
        │                                                        │
        └──► generated scaffolds (what to test) ─────────────────┤
                                                                 └──► end-to-end flows
        └──► Postman/Bruno · k6 · smoke · fuzz · mock                      │
                                                                  CI + Allure report
```

## Technical requirements

| Tool | Version | Needed for |
|------|---------|-----------|
| .NET SDK | 8.0 | everything |
| Docker (Desktop or Engine) with Compose v2 | recent | running the demo services |
| PowerShell | 5.1 or 7 | the example regeneration scripts |
| Python | 3.10+ | chapter 8 tooling (optional) |
| NSwag CLI | `dotnet tool install -g NSwag.ConsoleCore` | regenerating DTOs (optional) |
| Allure CLI | 2.x | viewing reports locally (optional) |
| k6 | recent | load tests (optional) |

All commands run from the repository root.

## Chapters

| # | Chapter | You will learn to… |
|---|---------|---------------------|
| 1 | [Running the example](01-run-the-example.md) | start the stack, run all suites, read the Allure report |
| 2 | [Onboarding a service](02-onboard-a-service.md) | commit a contract, get DTOs and routes, register the service in the three config files |
| 3 | [Generating test-data builders](03-generate-test-data-builders.md) | generate builders, build nested requests, customize with partial classes |
| 4 | [Writing steps](04-write-steps.md) | wrap endpoints in reusable, reportable steps |
| 5 | [Scaffolding and promoting tests](05-scaffold-and-promote-tests.md) | let the contract tell you what to test, turn a scaffold into real tests |
| 6 | [Authentication and identities](06-authentication-and-identities.md) | pick an auth mode, provision users and roles, test authorization rules |
| 7 | [End-to-end flows](07-end-to-end-flows.md) | compose steps into multi-service, multi-actor business journeys |
| 8 | [Beyond NUnit: collections, load, smoke, fuzz, mock](08-beyond-nunit.md) | reuse the same contract for Postman, k6, schemathesis and a mock server |
| 9 | [CI and reporting](09-ci-and-reporting.md) | run everything in GitHub Actions, gate stale generated code |
| 10 | [Adopting the framework for your product](10-adopt-for-your-product.md) | start a new product from this repository |

## Conventions

Each chapter is a set of **recipes** with the same sections:

- **Getting ready** — what you need before you start.
- **How to do it…** — numbered steps.
- **How it works…** — what happened behind the scenes.
- **There's more…** — variations and pitfalls.
- **See also** — reference pages.

> **Tip** boxes give shortcuts; **Important** boxes warn about mistakes that are easy to make.

Reference documentation lives next to the tutorial: [architecture](../architecture.md),
[core](../framework/core.md), [authentication](../framework/authentication.md),
[generators](../framework/generators.md), [Python tooling](../framework/python-tooling.md),
[CI](../ci.md).
