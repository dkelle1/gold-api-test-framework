---
applyTo: "framework/**"
---
Follow `framework/AGENTS.md`. In short: this is a reusable library — no service names, routes,
ports or DemoShop types; every behaviour change gets an offline test in
`framework/tests/ApiTestFramework.Framework.Tests`; generators stay deterministic; public API and
config schema changes are breaking (ask first); update `docs/framework/*.md` in the same change.
