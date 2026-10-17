---
name: write-e2e-flow
description: Writes an end-to-end business flow test that spans several services and actors by composing existing steps, builders and identities - with per-journey users, unique data, cleanup and business-level assertions. Use when asked for an e2e, journey, scenario, workflow or cross-service consistency test.
argument-hint: "[business journey in one sentence]"
---

# Write an end-to-end flow

An e2e flow is a different **composition** of the same steps the API tests use — never new HTTP
code. Reference implementation: `examples/demo-shop/api-tests/DemoShop.Tests/EndToEnd/CustomerJourneyTests.cs`;
background: `docs/tutorial/07-end-to-end-flows.md`.

## 1. Model the journey

Write the journey as 5–8 numbered business steps and list the **actors**:

| Actor kind | Identity |
|------------|----------|
| long-lived role (admin, back office) | suite identity from `GlobalSetup`: `.AsIdentity(TestIdentities.Admin)` |
| the protagonist (customer, partner) | fresh per journey: `_auth.RegisterIdentityAsync($"customer-{Guid.NewGuid():N}", role)` |

Check every step has a step method; add missing ones first (`add-api-tests`, step section).

## 2. Write it

```csharp
[TestFixture]
[AllureSuite("End-to-end")]
[AllureFeature("<Journey area>")]
public class <Journey>Tests : BaseTest
{
    protected override void OnFixtureSetUp() { /* one step instance per actor and service */ }

    [Test]
    [AllureSeverity(SeverityLevel.blocker)]
    public async Task <Actor>_<Does>_<Outcome>()
    {
        // 1. … (numbered comments = the business story)
    }
}
```

Rules:
- One step instance per actor: `var shop = new ProductServiceSteps().AsIdentity(customer.Identity);`
- Requests the journey is *about* → builders (`new CreateOrderRequestBuilder().ForProducts(...)`);
  intentional partial updates → plain DTOs (`new UpdateOrderRequest { Status = … }`).
- Expected rejections → `Try…` steps and assert status + message; happy steps fail fast by themselves.
- Assert the business outcome at each step and **invariants at the end** (totals, owner, nothing else
  changed). After a forbidden action, assert the state did not change.

## 3. Isolation

- Unique data per journey (`$"e2e-{Guid.NewGuid():N}"[..20]` categories, fresh users).
- `RegisterCleanup` right after every create, through an actor allowed to delete; a 404 during cleanup
  is fine (the journey may have deleted it).
- No ordering between tests, no shared mutable fields between test methods.
- Helpers used by several fixtures (`PublishProductAsync`, `RegisterCustomerAsync`) move to a
  `Flows/` folder in the Steps project.

## 4. Run

```bash
dotnet test examples/demo-shop/api-tests/DemoShop.Tests --filter "FullyQualifiedName~EndToEnd"
```

Against a deployed environment: `TEST_Services__<Name>__BaseUrl=…` per service and
`TEST_RetryCount=2` (retries are safe: POST is retried only when no response was received).

## Done when

- [ ] Story readable from the numbered comments and the Allure steps alone
- [ ] Only steps/builders/identities used — no raw HTTP
- [ ] Passes twice in a row on the same database; cleans up what it created
