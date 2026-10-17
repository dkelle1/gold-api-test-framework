# Chapter 9 — CI and reporting

A suite only protects you if it runs on every change. This chapter runs everything in GitHub
Actions, reads the results, and handles the one failure that is about the *framework* rather than
the product: stale generated code.

In this chapter:

- [Running every test on pull requests](#running-every-test-on-pull-requests)
- [Running a subset on demand](#running-a-subset-on-demand)
- [Reading the results](#reading-the-results)
- [Fixing "generated files are stale"](#fixing-generated-files-are-stale)

---

## Running every test on pull requests

### How to do it…

Nothing to do — [`.github/workflows/tests.yml`](../../.github/workflows/tests.yml) runs on every pull
request and push to `develop`:

1. **Build, framework unit tests & generated code** — no Docker: framework tests, a
   regenerate-and-diff check, the Python smoke runner against the mock.
2. **Example API tests (demo-shop stack)** — `docker compose up --wait`, the full `DemoShop.Tests`
   suite (API + e2e), live smoke & contract checks, Allure report.

### There's more…

Make both jobs required checks in the branch protection rule of `develop`, so a red suite blocks
the merge.

---

## Running a subset on demand

### How to do it…

GitHub → *Actions* → *Tests* → *Run workflow*: choose the branch, optionally an NUnit filter
(`FullyQualifiedName~EndToEnd`, `FullyQualifiedName~ProductService`…) and whether to run the smoke
checks.

---

## Reading the results

### How to do it…

1. The run page shows a summary per job: totals and the names and first error lines of failed
   tests.
2. Follow the *Allure report* link in the run summary (artifact `allure-report`), unzip it and open
   the single `index.html` — no server needed — for the
   full report with every request and response.
3. `service-logs.txt` holds the compose logs of all services; `live-swagger/` what the running
   services actually expose (compare it with the committed contract when a contract check fails).

---

## Fixing "generated files are stale"

### Getting ready

The first job failed with *Generated files are stale*: a contract changed but the generated
builders, scaffolds, collections or k6 scripts were not regenerated (or a `*.g.cs` was edited by hand).

### How to do it…

```powershell
./examples/demo-shop/api-tests/scripts/regenerate-all.ps1
python tools/generate_collections.py
python tools/generate_k6.py
git add -A; git commit -m "chore: regenerate from contract"
```

### How it works…

All generators are deterministic, so CI can regenerate and `git diff` (ignoring only the timestamp
line of scaffolds). Contract and generated code therefore always arrive in the same commit.

## See also

- [CI reference](../ci.md) — job details, artifacts, Jenkins, using the framework in your own pipeline
