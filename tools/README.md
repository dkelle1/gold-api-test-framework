# OpenAPI tooling (`tools/`)

Product-agnostic Python utilities driven by OpenAPI specs: Postman/Bruno collections, k6 load
scripts, a smoke & contract runner, a schemathesis fuzzing wrapper and an in-memory mock server.

Services, swagger location and output folders come from a JSON config
(`OPENAPI_TOOLS_CONFIG`, default: [`examples/demo-shop/api-tests/openapi-tools.json`](../examples/demo-shop/api-tests/openapi-tools.json)).

```bash
python3 tools/smoke_runner.py --list           # what would run
python3 tools/mock_server.py & python3 tools/smoke_runner.py
python3 tools/generate_collections.py && python3 tools/generate_k6.py
```

Full reference: **[docs/framework/python-tooling.md](../docs/framework/python-tooling.md)**.
