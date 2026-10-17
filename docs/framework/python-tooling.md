# Python tooling (`tools/`)

> **Scope:** these are *supporting utilities*, not a test layer. Functional, integration and
> end-to-end tests are written in .NET (NUnit). k6 (JavaScript) is used only for load testing.
> The smoke runner is a fast diagnostic gate ("is the environment up and roughly
> contract-compliant?") that complements — never replaces — the .NET suites.

Everything is driven by the committed OpenAPI specs; nothing is written per endpoint.
Python 3.10+, standard library only (schemathesis is optional).

| Tool | Purpose |
|------|---------|
| `generate_collections.py` | Postman + Bruno collections with the JWT flow pre-wired |
| `generate_k6.py` | k6 load-test scripts (smoke / load / stress profiles) |
| `smoke_runner.py` | live smoke + response-contract validation against running services |
| `run_schemathesis.py` | property-based fuzzing (needs `pip install -r tools/requirements.txt`) |
| `mock_server.py` | in-memory mock of every service, behaviour derived from the specs |
| `openapi_common.py` | shared library: config loading, spec parsing, example payloads, auth-flow discovery |

## Configuration — `openapi-tools.json`

The tools know no product. They read a config file chosen by the `OPENAPI_TOOLS_CONFIG`
environment variable; without it they use the demo shop's
[`examples/demo-shop/api-tests/openapi-tools.json`](../../examples/demo-shop/api-tests/openapi-tools.json).
Paths are relative to the config file.

```jsonc
{
  "swaggerDir": "MyProduct.Clients/swagger",
  "collectionName": "MyProduct",                   // Postman file / collection name
  "output": {
    "collections": "../collections",               // Postman + Bruno output
    "k6": "../perf/k6",                            // k6 scripts output
    "reports": "../TestResults"                    // schemathesis JUnit XML
  },
  "services": [                                    // processing order matters: token issuer and
    {                                              // dependencies (product before order) first
      "key": "auth",                               // CLI name: --service auth
      "name": "AuthService",
      "swagger": "auth-swagger.json",
      "baseUrl": "http://localhost:5300",          // default target
      "envVar": "AUTH_BASE_URL",                   // override at runtime
      "authIssuer": true                           // the service whose register/login issues tokens
    },
    { "key": "order", "name": "OrderService", "swagger": "order-swagger.json",
      "baseUrl": "http://localhost:5200", "envVar": "ORDER_BASE_URL" }
  ]
}
```

```bash
export OPENAPI_TOOLS_CONFIG=my-product/api-tests/openapi-tools.json
python3 tools/smoke_runner.py
```

**`.env`:** the tools load the same `.env` as the .NET tests (`TEST_ENV_FILE`, else the nearest `.env`
from the current directory upwards, else the repository root) without overriding real environment
variables. A service's base URL is taken from its tool variable (`AUTH_BASE_URL`, …), else from the
.NET setting `TEST_Services__<Name>__BaseUrl`, else from the config — so one `.env` line points both
the C# suite and the tools at an environment.

The token flow is discovered from the auth issuer's spec (a `register` or `login` operation whose
response contains a token). Products with a different auth scheme can still use the generators
and the mock; for k6 replace `obtainToken()` with `__ENV.TOKEN` in the generated script.

## Postman / Bruno collections — `generate_collections.py`

```bash
python3 tools/generate_collections.py            # both formats
python3 tools/generate_collections.py --postman  # Postman only
python3 tools/generate_collections.py --bruno    # Bruno only
```

Output (committed, deterministic): `<collections>/postman/<collectionName>.postman_collection.json`
+ `Local.postman_environment.json`, and `<collections>/bruno/` (open the folder in Bruno).

Pre-wired: collection-level Bearer auth reading `{{accessToken}}`; register/login requests store the
JWT automatically (unique usernames per send, so register never 409s); schema-derived bodies;
path parameters as variables. Import → send `POST /api/auth/register` once → everything else is
authorized.

## k6 load tests — `generate_k6.py`

```bash
python3 tools/generate_k6.py
k6 run examples/demo-shop/perf/k6/product-service.js                  # smoke profile (default)
k6 run -e PROFILE=load examples/demo-shop/perf/k6/all-services.js     # ramp to 20 VUs
k6 run -e PROFILE=stress examples/demo-shop/perf/k6/order-service.js  # ramp to 100 VUs
```

One script per service (chained CRUD: create → read → update → delete) plus `all-services.js`.
`setup()` registers a perf user and shares the JWT across VUs; **foreign keys are seeded
automatically at any nesting depth** (the order script creates a product and injects its id into
`items[].productId`). Thresholds fail the run at p95 ≥ 500 ms or error rate ≥ 1 %.

## Smoke & contract runner — `smoke_runner.py`

```bash
python3 tools/smoke_runner.py                  # everything, exit 1 on failure
python3 tools/smoke_runner.py --service product
python3 tools/smoke_runner.py --list           # print the plan, no requests
python3 tools/smoke_runner.py --json report.json
python3 tools/smoke_runner.py --role Admin     # register the smoke user with a role (or SMOKE_ROLE)
python3 tools/smoke_runner.py --no-auth-checks
```

Per run it registers a fresh user, executes every endpoint in CRUD order with schema-derived
payloads (chaining created ids into paths and foreign keys), asserts the documented success code,
**validates every response body against its schema** (types, enums, non-nullable fields) and checks
that protected GETs return 401 without a token. It takes seconds — in CI it found that the order
enums were documented as strings while the service sends integers.

## Fuzzing — `run_schemathesis.py`

```bash
pip install -r tools/requirements.txt
python3 tools/run_schemathesis.py                    # every service
python3 tools/run_schemathesis.py --service order --max-examples 200
```

Schemathesis generates boundary and malformed inputs per endpoint and checks the API never
returns 500, matches its response schemas and documented status codes. The wrapper registers a
user, injects the Bearer header, maps each spec to its base URL and writes JUnit XML to
`<reports>/schemathesis/`.

## Mock server — `mock_server.py`

```bash
python3 tools/mock_server.py                 # every service on its configured port
python3 tools/mock_server.py --service product
```

Behaviour comes from the specs: 401 without a Bearer header, in-memory CRUD with incrementing ids,
schema-conformant responses echoing the request. Roles and cross-service rules are **not**
enforced. Use it to develop collections, k6 scripts or new tooling without the real stack; the smoke
runner passes against it (the CI unit job does exactly this).

## Keeping generated artifacts fresh

The committed specs are the source of truth. After a contract change:

```bash
python3 tools/generate_collections.py && python3 tools/generate_k6.py
```

CI fails when the committed collections or k6 scripts differ from what the generators produce
([CI](../ci.md)).
