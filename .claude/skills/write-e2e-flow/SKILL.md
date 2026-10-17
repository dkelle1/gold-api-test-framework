---
name: write-e2e-flow
description: Generates the complete end-to-end test for a business flow described in a flow file (flows/*.md) or in plain words - turns the description into a flow file if needed, takes every expectation from the flow and the domain rules (never from observed responses), composes existing steps, builders and identities, runs it, and reports any disagreement between system and flow instead of bending the test. Use when asked for an e2e, journey, scenario, workflow or cross-service test.
argument-hint: "[path to flows/*.md | journey description]"
---

# Generate an end-to-end flow test

Input: `$ARGUMENTS` — a flow file (`<product>/api-tests/flows/*.md`) or a description in words.
Reference implementation: `examples/demo-shop/api-tests/DemoShop.Tests/EndToEnd/CustomerJourneyTests.cs`;
background: `docs/tutorial/07-end-to-end-flows.md`.

**The oracle rule.** Expected outcomes come from the flow file and the product's `domain-rules.md` —
never from what the system returns. If a run disagrees, you report it; you do not change the
expectation to make the test pass.

## 1. Get a complete flow file

- Given a file: read it, the product's `api-tests/domain-rules.md` and the product README.
- Given words: create `flows/<kebab-name>.md` from [flow-template.md](flow-template.md), fill what the
  user said, cite rule IDs. **Every step needs an expected outcome.** Anything you would have to guess
  (an amount, a status, "is it rejected or ignored?") goes under *Open questions* — ask the user and
  stop until it is answered, unless a rule settles it.
- Add the flow to `flows/README.md` (status `ready`).

## 2. Check the building blocks

For each step: is there a step method (`DemoShop.Steps/ServiceSteps/*Steps.cs`), a builder or data
generator for its request, an identity for its actor? Missing step methods are added first, following
the conventions (`add-api-tests` skill, step section) — never raw HTTP in the flow.

| Actor kind | Identity |
|------------|----------|
| long-lived role (admin, back office) | suite identity: `new XSteps().AsIdentity(TestIdentities.Admin)` |
| protagonist (customer, partner) | fresh per run: `_auth.RegisterIdentityAsync($"customer-{Guid.NewGuid():N}", role)` |

## 3. Write the test

File `DemoShop.Tests/EndToEnd/<Flow>Tests.cs` (or a new method in an existing fixture of the same area):

```csharp
/// <summary>…goal of the flow…</summary>
/// Flow: examples/demo-shop/api-tests/flows/<file>.md (FLOW-XXX-NN)
[TestFixture]
[AllureSuite("End-to-end")]
[AllureFeature("<area>")]
public class <Flow>Tests : BaseTest
{
    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("FLOW-XXX-NN: <goal>")]
    public async Task <Actor>_<Does>_<Outcome>()
    {
        // 1. <step 1 of the flow, same number>                     (R-…)
        …
    }
}
```

- One numbered comment per flow step, same numbers, rule IDs where the flow has them.
- One step instance per actor; requests the flow is *about* → builders; partial updates → plain DTOs
  (`new UpdateOrderRequest { Status = … }`).
- Assert each step's expected outcome in business terms (amounts, states, visibility, rejections) and
  the end invariants. Contract conformance of every response is checked automatically — don't
  re-assert field types.
- Expected rejections → `Try…` steps + status + message; after a forbidden action assert nothing changed.
- Asynchronous effects → `Eventually.UntilAsync` / `Eventually.SatisfiesAsync` (FluentAssertions inside),
  never `Thread.Sleep`.
- Own your data (GUID-suffixed values, fresh users); `RegisterCleanup` right after each create via an
  actor allowed to delete.

## 4. Build, run, compare with the flow

```bash
dotnet build ApiTestFramework.sln
dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~<Flow>Tests"
```

(no local Docker → push and read the CI run with `diagnose-test-failure`).

On failure, classify before touching anything:

| Cause | Action |
|-------|--------|
| test bug (wrong step, wrong data, typo) | fix the test |
| contract violation at a request | contract vs service question — `update-api-contract` or report |
| **system disagrees with the flow / a rule** | keep the expectation; report the deviation (step, expected, actual, rule) and ask whether the system or the flow is wrong |

## 5. Close the loop

- Flow file: *Status* `implemented`, *Test* filled in; `flows/README.md` row updated.
- Report: what was generated, steps added, runs needed, and every deviation or open question.

## Done when

- [ ] Every flow step is a numbered block in the test; every expectation traces to the flow or a rule
- [ ] Only steps, builders, identities, `Eventually` — no raw HTTP, no sleeps
- [ ] Passes twice in a row on the same database (or deviations are reported, not hidden)
- [ ] Flow file and flows index updated
