# Demo shop — domain rules

The **intended** business behaviour of the demo shop, written as numbered rules. End-to-end flows
(`flows/*.md`) and their tests take their expectations from here and from the flow descriptions —
**never from what the system happens to return**. When a test shows the system disagreeing with a
rule, that is a finding: either the system has a bug or the rule is wrong, and a human decides which.

Rules marked *(demo simplification)* are deliberate shortcuts of the example; real products would
differ (see `../README.md`).

## Accounts and access — AuthService

| ID | Rule |
|----|------|
| R-AUTH-1 | Anyone can register; the role (`User`, `Admin`) is taken from the registration request *(demo simplification)*. |
| R-AUTH-2 | Username and e-mail are unique: registering an existing one is rejected with 409. |
| R-AUTH-3 | Logging in with a wrong password is rejected with 401. |
| R-AUTH-4 | Every Product and Order operation requires a valid token; without one the answer is 401. |

## Catalog — ProductService

| ID | Rule |
|----|------|
| R-PROD-1 | Any authenticated user may create and update products. Only `Admin` may delete them: other users get 403 and the product is unchanged; anonymous callers get 401. |
| R-PROD-2 | A product needs a name and a price greater than zero (400 otherwise). |
| R-PROD-3 | The product list and the category listing show **active** products only; the category must match exactly. |
| R-PROD-4 | A deactivated product (`isActive = false`) is still readable by id. |
| R-PROD-5 | A price change applies to the product immediately (next read shows the new price). |

## Orders — OrderService

| ID | Rule |
|----|------|
| R-ORD-1 | An order needs a customer with name and e-mail and at least one item with quantity > 0 (400 otherwise). |
| R-ORD-2 | Every item must reference an existing, **active** product with enough stock for the summed quantity of that product in the order; otherwise the whole order is rejected with 400 and nothing is created. |
| R-ORD-3 | The product name and unit price are **captured when the order is placed**; later changes to the product (price, name, deactivation, deletion) do not change existing orders. |
| R-ORD-4 | Line total = unit price × quantity; order total = sum of line totals. |
| R-ORD-5 | A new order is `Pending`. Updates are partial: changing the status does not change customer, items, shipping or totals. |
| R-ORD-6 | Without an explicit shipping address, the order ships to the customer's address. |
| R-ORD-7 | Placing an order does not change the product's stock *(demo simplification)*. |
| R-ORD-8 | A customer's order history is found by the exact e-mail used on the orders. |
| R-ORD-9 | Any authenticated user may read, change or delete any order — orders have no owner *(demo simplification)*. |

## Errors

| ID | Rule |
|----|------|
| R-ERR-1 | 400, 404 and 409 responses carry `{"message": "..."}` describing the problem (documented as `ErrorResponse`). |
