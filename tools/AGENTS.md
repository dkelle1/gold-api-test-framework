# AGENTS.md — tools/

Product-agnostic Python utilities driven by OpenAPI specs (collections, k6, smoke runner,
schemathesis wrapper, mock server). Read the root `AGENTS.md` first; these rules add to it.

## Rules

- **Standard library only** (Python 3.10+). `schemathesis` is the single optional dependency
  (`requirements.txt`) and only `run_schemathesis.py` may import it. Adding dependencies: ask first.
- **No product knowledge in code.** Services, swagger location, output dirs and the auth issuer come
  from `openapi-tools.json` (`OPENAPI_TOOLS_CONFIG`, default: the demo-shop config). Shared logic
  belongs in `openapi_common.py`.
- **Deterministic output** for generators (`generate_collections.py`, `generate_k6.py`): same specs →
  byte-identical files. CI regenerates and diffs.
- Every entry point calls `configure_console()` first (UTF-8 output on Windows consoles); keep
  messages ASCII-friendly.
- Exit codes matter: smoke/fuzz runners exit non-zero on failures (CI relies on it).
- Generated artifacts are written to the configured output dirs — never hand-edit them.

## Verify

```bash
python tools/smoke_runner.py --list
python tools/generate_collections.py && python tools/generate_k6.py && git diff --stat -- examples/
python tools/mock_server.py &   python tools/smoke_runner.py      # must pass against the mock
```

## Document

Behaviour or config changes update `docs/framework/python-tooling.md`.
