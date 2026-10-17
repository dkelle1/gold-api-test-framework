---
name: diagnose-test-failure
description: Diagnoses a failing or flaky test run locally or in GitHub Actions - gathers the run summary, TRX, Allure attachments, service logs and live swagger, classifies the cause (product bug, test bug, contract drift, environment, framework) and proposes the right fix without weakening tests. Use when tests fail, CI is red, a test is flaky, or "generated files are stale" appears.
argument-hint: "[CI run id | test name | error message]"
---

# Diagnose a test failure

Evidence first, fix second. Never make a test pass by deleting assertions, adding `[Ignore]`,
`Thread.Sleep` or blind retries.

## 1. Collect evidence

**CI (GitHub Actions)**

```bash
gh run list --branch <branch> --limit 5
gh run view <run-id>                                    # jobs + failed steps
gh run view <run-id> --log-failed                       # failing step logs
gh run download <run-id> -n integration-test-results -D ./ci-artifacts
```

Artifacts: `test-results/*.trx`, `allure-report/index.html` (every request/response),
`service-logs.txt` (docker compose logs), `live-swagger/` (what services expose), smoke JSON reports.
The run page summary lists failed tests with their first error line.

**Local**

```bash
dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~<Test>" --logger "console;verbosity=detailed"
docker compose -f examples/demo-shop/docker-compose.yml logs --tail=200 <service>
```

Allure results: `examples/demo-shop/api-tests/DemoShop.Tests/bin/Debug/net8.0/allure-results`.

## 2. Classify

| Symptom | Likely cause | Fix in |
|---------|-------------|--------|
| `Generated files are stale` (unit job) | contract changed without regenerating, or a generated file was edited | `update-api-contract` |
| smoke: `value X not in enum`, `expected string, got int`, 400 on generated payloads | contract drift (contract ≠ service) | contract (or the service, if the contract is the intended truth — ask) |
| `Contract violation(s) for METHOD /route → status` at a request | the response differs from the committed contract (undocumented status/body, type, enum, missing/extra field) | decide which side is wrong: update the contract (`update-api-contract`) or report the service bug — never `WithoutResponseValidation()` to get green |
| test gets 401 | token not provisioned / wrong identity / `WithoutAuth` misuse | `GlobalSetup`, identities, step binding |
| 403 where 2xx expected | wrong actor (e.g. product cleanup not as admin) | step binding `AsIdentity(TestIdentities.Admin)` |
| 409 on create, counts off, "already exists" | test data not unique / leaked from earlier runs | unique values + `RegisterCleanup` |
| passes alone, fails in the suite | shared static state (`TokenProvider`, `AuthenticationContext`) or ordering | restore state; remove ordering assumptions |
| connection refused / timeouts at start | stack not healthy | `docker compose ps`, service logs; CI `--wait` step |
| all tests of one fixture `broken` with "Fixture setup failed" | exception in `OnFixtureSetUp` | the root cause in the message |
| wrong status/body consistently, contract and test agree | **product bug** | report it; keep the test red or mark with a linked issue — ask |
| failure inside `ApiTestFramework.*` | framework bug | `extend-framework`, with a unit test reproducing it |

Reproduce once more before concluding "flaky": run the single test 5× (`for i in 1 2 3 4 5; do dotnet test … ; done`)
and note the failure rate and whether the failure mode is identical.

## 3. Report

State: failing test(s) · evidence (status, body excerpt, log line) · classification · proposed fix ·
what you verified after the fix (paste the summary). If the root cause is in the product or is
ambiguous, stop and ask before changing assertions or service code.
