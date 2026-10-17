# Flow: Catalog changes after an order is placed

| | |
|---|---|
| **ID** | FLOW-SHOP-03 |
| **Status** | implemented |
| **Test** | `DemoShop.Tests/EndToEnd/CatalogChangesAfterOrderTests.cs` → `CatalogChanges_DoNotAlterPlacedOrders_ButApplyToNewOnes` |
| **Rules** | R-PROD-3, R-PROD-4, R-PROD-5, R-ORD-2, R-ORD-3, R-ORD-4, R-ORD-8 |

## Goal

Customers are charged what the product cost when they ordered it. Price changes and deactivation
affect only what happens next — never orders that are already placed.

## Actors

| Actor | Identity | Notes |
|-------|----------|-------|
| Catalog admin | suite identity `admin` | publishes, reprices and deactivates the product |
| Customer | fresh per run (role `User`) | places orders |

## Preconditions and data

- One product "Desk lamp" in a category nobody else uses, price **100.00**, stock 50, active.

## Steps

| # | Actor | Action | Expected outcome | Rule |
|---|-------|--------|------------------|------|
| 1 | Catalog admin | publishes the desk lamp at 100.00 | product exists, active, price 100.00 | |
| 2 | Customer | registers | gets an account | R-AUTH-1 |
| 3 | Customer | orders 2 desk lamps | order A is placed: unit price 100.00, line total 200.00, total 200.00 | R-ORD-4 |
| 4 | Catalog admin | raises the price to 150.00 | the product now costs 150.00 | R-PROD-5 |
| 5 | Customer | opens order A | still unit price 100.00 and total 200.00 | R-ORD-3 |
| 6 | Customer | orders 1 desk lamp | order B is placed at the new price: unit price 150.00, total 150.00 | R-ORD-3, R-ORD-4 |
| 7 | Catalog admin | deactivates the desk lamp | the product is still readable by id, marked inactive | R-PROD-4 |
| 8 | Customer | browses the category | the desk lamp is no longer listed | R-PROD-3 |
| 9 | Customer | tries to order 1 desk lamp | rejected with 400; no order is created | R-ORD-2 |
| 10 | Customer | opens their order history | exactly orders A and B, with totals 200.00 and 150.00 | R-ORD-3, R-ORD-8 |

## Invariants at the end

- Order A still has unit price 100.00 and total 200.00; order B 150.00 / 150.00.
- The rejected attempt in step 9 left no trace in the order history.

## Out of scope

- Stock levels (orders do not change stock in the demo — R-ORD-7).
- Deleting the product (FLOW-SHOP-02).

## Open questions

- None.
