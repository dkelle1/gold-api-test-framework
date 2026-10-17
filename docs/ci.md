# Continuous integration

Two pipelines ship with the repository. Both run the same things; GitHub Actions is the one that
guards pull requests.

## GitHub Actions — `.github/workflows/tests.yml`

Triggers: every pull request and push to `develop`, plus **manual runs** (Actions → *Tests* →
*Run workflow*) with two inputs: an NUnit `filter` for the example suite
(e.g. `FullyQualifiedName~EndToEnd`) and whether to run the live smoke checks.

| Job | Steps | Needs Docker |
|-----|-------|--------------|
| **Build, framework unit tests & generated code** | build the solution · `ApiTestFramework.Framework.Tests` · regenerate builders/scaffolds (`Generator.Cli` + `generator.json`) and Python artifacts, **fail if anything differs from the commit** · Python smoke runner against the in-memory mock | no |
| **Example API tests (demo-shop stack)** | `docker compose up --wait` (SQL Server, Redis, 3 services) · save the live swagger · `DemoShop.Tests` (API + e2e) · smoke & contract checks against the live stack (`--role Admin`) · Allure HTML report | yes |

Each run publishes a Markdown summary (totals and failed test names, from TRX via
[`scripts/ci/trx-summary.py`](../scripts/ci/trx-summary.py)) and uploads artifacts:

| Artifact | Contents |
|----------|----------|
| `unit-test-results` | framework TRX, mock smoke report |
| **`allure-report`** | the Allure report as **one self-contained `index.html`** (attachments embedded) — unzip and open it in a browser, no server or Allure CLI needed; the run summary links to it |
| `integration-test-results` | example TRX, raw `allure-results/`, `contract-coverage/` (also appended to the run summary), smoke JSON, `live-swagger/` (what the running services expose — handy to compare with the curated contract), `service-logs.txt` |

### The "generated code is up to date" gate

Builders and Python artifacts are deterministic, so CI can regenerate them and `git diff` the
result (scaffolds are compared ignoring their `// Generated:` timestamp line). If the gate fails,
run locally and commit:

```powershell
./examples/demo-shop/api-tests/scripts/regenerate-all.ps1
python tools/generate_collections.py; python tools/generate_k6.py
```

### Protecting the branch

Make both jobs required status checks of `develop` (Settings → Branches → branch protection rule),
so nothing merges without passing the whole suite.

## Jenkins — `Jenkinsfile`

Same shape on a Docker-capable agent: build → `docker compose up --wait` (with
`COMPOSE_FILE=examples/demo-shop/docker-compose.yml` and a per-build `COMPOSE_PROJECT_NAME`) →
framework tests + example tests in an SDK container joined to the compose network → Allure +
JUnit publishing → `docker compose down -v`.

Inside the compose network the tests address services by name, overriding the localhost URLs from
`appsettings.test.json`:

```bash
export TEST_Services__AuthService__BaseUrl=http://auth-service:8080
export TEST_Services__ProductService__BaseUrl=http://product-service:8080
export TEST_Services__OrderService__BaseUrl=http://order-service:8080
```

## Running your own product's tests in CI

The framework needs nothing CI-specific:

```bash
dotnet test path/to/MyProduct.Tests --logger "trx;LogFileName=results.trx"
```

- Point the suite at an environment with `TEST_ENVIRONMENT=<name>` (loads
  `appsettings.test.<name>.json`) or individual `TEST_...` variables.
- Pass secrets as `TEST_Authentication__...` variables from the CI secret store.
- Collect Allure results from `bin/<Configuration>/net8.0/allure-results` (Allure.NUnit 2.12
  ignores `ALLURE_RESULTS_DIRECTORY`).
- Keep `RetryCount` at 0 for API suites; consider `TEST_RetryCount=2` for e2e runs against shared
  environments.
