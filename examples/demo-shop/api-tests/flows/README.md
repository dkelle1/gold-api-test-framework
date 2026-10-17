# End-to-end flows

Each file describes one business flow in plain language — actors, steps, expected outcomes —
following [the flow template](../../../../.claude/skills/write-e2e-flow/flow-template.md).
The test code in `DemoShop.Tests/EndToEnd/` is generated from these descriptions (with the
`write-e2e-flow` skill) and links back to them; expectations come from the flow and from
[`domain-rules.md`](../domain-rules.md), never from observed responses.

| ID | Flow | Status | Test |
|----|------|--------|------|
| FLOW-SHOP-01 | [New customer orders and receives a shipment](customer-journey.md#flow-shop-01--new-customer-orders-and-receives-a-shipment) | implemented | `CustomerJourneyTests.NewCustomer_BrowsesOrdersAndReceivesShipment` |
| FLOW-SHOP-02 | [A product removed from the catalog can no longer be ordered](customer-journey.md#flow-shop-02--a-product-removed-from-the-catalog-can-no-longer-be-ordered) | implemented | `CustomerJourneyTests.ProductRemovedFromCatalog_CanNoLongerBeOrdered` |
| FLOW-SHOP-03 | [Catalog changes after an order is placed](catalog-changes-after-order.md) | implemented | `CatalogChangesAfterOrderTests.CatalogChanges_DoNotAlterPlacedOrders_ButApplyToNewOnes` |

## Adding a flow

1. Copy the template to `flows/<kebab-name>.md`, fill in actors, steps and **expected outcomes**,
   referencing rule IDs. Unknown expectations go under *Open questions* — don't leave them to the
   test author.
2. In Claude Code: `/write-e2e-flow examples/demo-shop/api-tests/flows/<kebab-name>.md`
   (other agents: follow `.claude/skills/write-e2e-flow/SKILL.md`).
3. Review the generated test like any code; the flow file's *Status* and *Test* are updated with it.
4. If the run disagrees with the flow, the PR says so — someone decides whether the system or the
   flow is wrong. The test is never bent to match observed behaviour.
