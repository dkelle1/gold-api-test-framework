# Flow: <short business title>

| | |
|---|---|
| **ID** | FLOW-<AREA>-<NN> |
| **Status** | draft · ready · implemented |
| **Test** | `<Product>.Tests/EndToEnd/<Fixture>.cs` → `<TestMethod>` (filled in when implemented) |
| **Rules** | R-…, R-… (from the product's `domain-rules.md`) |

## Goal

One or two sentences: which business outcome this flow protects and why it matters.

## Actors

| Actor | Identity | Notes |
|-------|----------|-------|
| e.g. Catalog admin | suite identity `admin` | publishes and changes products |
| e.g. Customer | fresh per run (role `User`) | the protagonist |

## Preconditions and data

- What must exist before step 1 and who creates it (always created by the flow itself, never assumed).
- Values that matter for the expectations (prices, quantities, statuses). Everything else may be random.

## Steps

| # | Actor | Action | Expected outcome (business language) | Rule |
|---|-------|--------|--------------------------------------|------|
| 1 | … | … | … | R-… |
| 2 | … | … | … | R-… |

Write the **expected outcome** for every step from the business point of view: amounts, states,
visibility, rejections. "Works" or "succeeds" is not an expectation. If you are not sure what the
system should do, write the question here instead of an expectation — the test author must not guess.

## Invariants at the end

- Facts that must still hold after the whole flow (totals unchanged, nothing leaked, …).

## Out of scope

- What this flow deliberately does not check (covered elsewhere, or not a requirement).

## Open questions

- Anything the implementation surfaced that the business has to decide.
