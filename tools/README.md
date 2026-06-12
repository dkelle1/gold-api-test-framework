# Generic OpenAPI Tooling (`tools/`)

Python tooling driven entirely by the committed OpenAPI specs in
`tests/ApiTestFramework.Clients/swagger/`. Nothing here is hand-written per
endpoint: **adding a new microservice to the framework only requires a new
entry in `SERVICES` inside `openapi_common.py`** — every tool below picks it
up automatically.

| Tool | Purpose | Dependencies |
|------|---------|--------------|
| `generate_collections.py` | Postman + Bruno collections with wired JWT auth | stdlib only |
| `generate_k6.py` | k6 load-test scripts (smoke/load/stress profiles) | stdlib only |
| `smoke_runner.py` | Live smoke + contract validation against running services | stdlib only |
| `run_schemathesis.py` | Property-based fuzzing / contract testing | `pip install -r tools/requirements.txt` |
| `mock_server.py` | In-memory mock of every service, behaviour derived from the specs | stdlib only |
| `openapi_common.py` | Shared library: service registry, spec parsing, example-payload generation, auth-flow discovery | stdlib only |

All tools honour base-URL overrides via environment variables
(`AUTH_BASE_URL`, `PRODUCT_BASE_URL`, `ORDER_BASE_URL`); defaults match the
docker-compose port mappings (`:5300`, `:5100`, `:5200`).

---

## Postman / Bruno collections — `generate_collections.py`

```bash
python3 tools/generate_collections.py            # both formats
python3 tools/generate_collections.py --postman  # Postman only
python3 tools/generate_collections.py --bruno    # Bruno only
```

Output (committed, deterministic — regenerating gives clean diffs):

- `collections/postman/ApiTestFramework.postman_collection.json` + `Local.postman_environment.json`
- `collections/bruno/` — open the folder directly in [Bruno](https://www.usebruno.com/)

What's pre-wired:

- collection-level **Bearer auth** reading `{{accessToken}}`
- the **Register/Login requests capture the JWT** into the environment
  automatically (post-response script), with unique usernames per send so
  register never 409s
- request bodies are realistic examples derived from the schemas
- path parameters exposed as variables

Workflow: import collection + environment → send `POST /api/auth/register`
once → every other request is authorized.

## k6 load tests — `generate_k6.py`

```bash
python3 tools/generate_k6.py     # regenerate perf/k6/*.js from the specs
```

Run (requires [k6](https://k6.io)):

```bash
k6 run perf/k6/product-service.js                  # smoke profile (default)
k6 run -e PROFILE=load perf/k6/all-services.js     # ramp to 20 VUs, 3 min
k6 run -e PROFILE=stress perf/k6/order-service.js  # ramp to 100 VUs, 6 min
```

Generated scripts:

- `auth-service.js` — register + login throughput scenario
- `product-service.js`, `order-service.js` — chained CRUD flows
  (create → read → update → delete) with a per-request status check
- `all-services.js` — everything combined

Built-in behaviour:

- `setup()` registers a unique perf user and shares the JWT across VUs
- **cross-service foreign keys are seeded automatically** — the order script
  creates a product first and injects its id into `CreateOrderRequest.productId`
- thresholds fail the run when p95 latency ≥ 500 ms or error rate ≥ 1 %

## Smoke & contract runner — `smoke_runner.py`

```bash
python3 tools/smoke_runner.py                  # everything, exit 1 on failure
python3 tools/smoke_runner.py --service product
python3 tools/smoke_runner.py --list           # print the plan, no requests
python3 tools/smoke_runner.py --json report.json
```

Per run it:

1. registers a fresh user and captures the JWT,
2. executes every endpoint in natural CRUD order with schema-derived payloads,
   chaining created ids into `{id}` paths and foreign keys
   (`order.productId` ← the product it just created),
3. asserts the status code declared in the spec,
4. **validates the response body against the response schema**
   (types, enums, non-nullable fields),
5. asserts protected GET endpoints return **401 without a token**.

Designed as a fast CI gate (a few seconds) before the full .NET suite.

## Fuzz / property-based testing — `run_schemathesis.py`

```bash
pip install -r tools/requirements.txt
python3 tools/run_schemathesis.py                    # fuzz every service
python3 tools/run_schemathesis.py --service order --max-examples 200
```

[Schemathesis](https://schemathesis.readthedocs.io/) generates hundreds of
boundary/malformed cases per endpoint and checks the API never 500s, matches
its declared response schemas and honours documented status codes. The wrapper
adds what the CLI can't know: it registers a user, injects the
`Authorization: Bearer …` header, maps each spec to its live base URL and
writes JUnit XML to `TestResults/schemathesis/` (CI-friendly).

## Mock server — `mock_server.py`

```bash
python3 tools/mock_server.py                 # all services on :5300/:5100/:5200
python3 tools/mock_server.py --service product
```

Serves every registered service on its real port with behaviour derived
entirely from the spec: JWT enforcement (401 without a Bearer header),
in-memory CRUD with auto-increment ids, schema-conformant responses that echo
back the request payload. Useful for developing tests, collections and k6
scripts without SQL Server + Redis + three .NET services — the smoke runner
passes 29/29 checks against it:

```bash
python3 tools/mock_server.py &  python3 tools/smoke_runner.py
```

---

## CI integration sketch

The smoke runner and schemathesis emit proper exit codes and JUnit XML, so a
Jenkins stage after `docker compose up --wait` is a one-liner each:

```groovy
stage('Smoke (OpenAPI)') {
    steps { sh 'python3 tools/smoke_runner.py --json TestResults/smoke.json' }
}
stage('Fuzz (Schemathesis)') {
    steps { sh 'pip install -r tools/requirements.txt && python3 tools/run_schemathesis.py --max-examples 30' }
    post { always { junit 'TestResults/schemathesis/*.xml' } }
}
```

## Keeping specs fresh

The committed swagger files are the single source of truth for these tools.
After changing a service's endpoints, refresh them (see
`scripts/refresh-swagger.ps1`) and rerun the generators:

```bash
python3 tools/generate_collections.py && python3 tools/generate_k6.py
```
