# Chapter 8 — Beyond NUnit: collections, load, smoke, fuzz, mock

The contract you committed in chapter 2 drives more than C# code. The Python tools in `tools/`
turn it into everything around the test suite: collections for manual exploration, load tests, a
fast contract gate, fuzzing, and a mock of the whole system.

In this chapter:

- [Exploring the API with Postman or Bruno](#exploring-the-api-with-postman-or-bruno)
- [Load-testing with k6](#load-testing-with-k6)
- [A contract smoke check in seconds](#a-contract-smoke-check-in-seconds)
- [Fuzzing with schemathesis](#fuzzing-with-schemathesis)
- [Developing without the real stack](#developing-without-the-real-stack)

### Getting ready (all recipes)

Python 3.10+. The tools read `examples/demo-shop/api-tests/openapi-tools.json`; for your product
set `OPENAPI_TOOLS_CONFIG` to your own file ([chapter 2](02-onboard-a-service.md#registering-the-service)).

---

## Exploring the API with Postman or Bruno

### How to do it…

1. `python tools/generate_collections.py`
2. Import `examples/demo-shop/collections/postman/DemoShop.postman_collection.json` and
   `Local.postman_environment.json` into Postman — or open `examples/demo-shop/collections/bruno/` in Bruno.
3. Send *POST /api/auth/register* once. Every other request is now authorized.

### How it works…

The register/login requests carry a post-response script that stores the JWT in `{{accessToken}}`;
the collection uses it for Bearer auth. Bodies are examples derived from the schemas.

---

## Load-testing with k6

### How to do it…

```bash
python tools/generate_k6.py
k6 run examples/demo-shop/perf/k6/order-service.js                    # smoke: 2 VUs, 30 s
k6 run -e PROFILE=load examples/demo-shop/perf/k6/all-services.js     # ramp to 20 VUs
```

### How it works…

Each script registers a perf user in `setup()`, then loops a CRUD flow. Foreign keys are seeded
automatically: the order script creates a product first and injects its id into
`items[].productId`. Thresholds (p95 < 500 ms, errors < 1 %) make k6 exit non-zero on regressions.

---

## A contract smoke check in seconds

### How to do it…

```bash
python tools/smoke_runner.py --role Admin
```

### How it works…

The runner calls every operation in CRUD order with schema-derived payloads, checks the documented
status code, **validates each response body against its schema**, and verifies 401 without a
token. `--role Admin` registers its user as an admin so admin-only operations pass.

### There's more…

It runs before the NUnit suite in CI and is excellent at catching contract drift: it is how the
"order enums are integers, the contract said strings" mismatch was found — the C# suite could not
see it, because the C# DTOs and the service agreed with each other.

---

## Fuzzing with schemathesis

### How to do it…

```bash
pip install -r tools/requirements.txt
python tools/run_schemathesis.py --service order --max-examples 100
```

### How it works…

Schemathesis generates boundary and malformed inputs from the schemas and flags 500s and responses
that violate the contract. The wrapper adds a token and the base URLs, and writes JUnit XML.

---

## Developing without the real stack

### How to do it…

```bash
python tools/mock_server.py &        # every service on its configured port
python tools/smoke_runner.py         # passes against the mock
```

### How it works…

The mock serves in-memory CRUD with 401 enforcement and schema-shaped responses. It does **not**
know business rules (roles, stock, cross-service validation), so use it for tooling and test
scaffolding work — not as a substitute for the real API tests.

## See also

- [Python tooling reference](../framework/python-tooling.md)
