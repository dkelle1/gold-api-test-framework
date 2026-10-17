---
applyTo: "tools/**"
---
Follow `tools/AGENTS.md`. In short: Python 3.10+ standard library only (schemathesis optional);
no product knowledge — everything comes from `openapi-tools.json`; generator output must be
deterministic; call `configure_console()` in every entry point; update
`docs/framework/python-tooling.md` when behaviour changes.
