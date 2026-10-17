# Customer journeys

Two flows implemented in `DemoShop.Tests/EndToEnd/CustomerJourneyTests.cs` (written before flow files
existed; described here retroactively).

---

## FLOW-SHOP-01 — New customer orders and receives a shipment

| | |
|---|---|
| **ID** | FLOW-SHOP-01 |
| **Status** | implemented |
| **Test** | `DemoShop.Tests/EndToEnd/CustomerJourneyTests.cs` → `NewCustomer_BrowsesOrdersAndReceivesShipment` |
| **Rules** | R-PROD-3, R-ORD-3, R-ORD-4, R-ORD-5, R-ORD-8 |

### Goal

A brand-new customer can find products, order them and follow the order until it ships, while the
back office moves it through its statuses.

### Actors

| Actor | Identity | Notes |
|-------|----------|-------|
| Catalog admin | suite identity `admin` | publishes products |
| Back office | suite identity `admin` | confirms and ships orders |
| Customer | fresh per run (role `User`) | the protagonist |

### Preconditions and data

- Two products in a category nobody else uses: keyboard 49.99, mouse 19.50, stock 100 each.

### Steps

| # | Actor | Action | Expected outcome | Rule |
|---|-------|--------|------------------|------|
| 1 | Catalog admin | publishes the keyboard and the mouse | both exist and are active | |
| 2 | Customer | registers | gets an account and a token | R-AUTH-1 |
| 3 | Customer | browses the category | sees exactly the two products | R-PROD-3 |
| 4 | Customer | orders 1 keyboard + 2 mice, express shipping | order is `Pending`, total 88.99, shipping express | R-ORD-4, R-ORD-5 |
| 5 | Customer | opens their order history | sees exactly this order | R-ORD-8 |
| 6 | Back office | confirms, then ships the order | each update succeeds | R-ORD-5 |
| 7 | Customer | opens the order | status `Shipped`; total and customer unchanged | R-ORD-5 |

### Invariants at the end

- The order total is the one computed when it was placed.

---

## FLOW-SHOP-02 — A product removed from the catalog can no longer be ordered

| | |
|---|---|
| **ID** | FLOW-SHOP-02 |
| **Status** | implemented |
| **Test** | `DemoShop.Tests/EndToEnd/CustomerJourneyTests.cs` → `ProductRemovedFromCatalog_CanNoLongerBeOrdered` |
| **Rules** | R-PROD-1, R-ORD-2 |

### Goal

Removing a product is reserved to admins, and once removed it cannot be ordered.

### Steps

| # | Actor | Action | Expected outcome | Rule |
|---|-------|--------|------------------|------|
| 1 | Catalog admin | publishes a product | product exists | |
| 2 | Customer | tries to delete the product | rejected with 403 | R-PROD-1 |
| 3 | Catalog admin | deletes the product | succeeds (204) | R-PROD-1 |
| 4 | Customer | orders the deleted product | rejected with 400, the message names the product id as not found | R-ORD-2 |
