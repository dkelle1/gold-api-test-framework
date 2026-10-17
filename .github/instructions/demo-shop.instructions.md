---
applyTo: "examples/demo-shop/**"
---
Follow `examples/demo-shop/AGENTS.md`. In short: `services/` is the system under test (change only
when asked); the swagger files in `DemoShop.Clients/swagger/` are a curated contract (required
fields, 401/403, enums as integers); never edit `*.g.cs`, `Generated/*.cs.txt`, `collections/`,
`perf/k6/` — regenerate; tests use steps and identities (`AsIdentity(TestIdentities.Admin)`),
unique data and `RegisterCleanup`; product deletes must run as admin.
