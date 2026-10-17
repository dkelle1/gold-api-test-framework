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

## Machine clients — shared authorization server (AuthService)

Test suites and other services call the APIs as **OAuth2 clients**, one client per group of
services, instead of as users.

| ID | Rule |
|----|------|
| R-CLIENT-1 | `POST /connect/token` issues tokens to registered clients with `grant_type=client_credentials` (form-encoded; credentials as form fields or HTTP Basic). Unknown client or wrong secret: 401 `invalid_client`; any other grant type: 400 `unsupported_grant_type`. |
| R-CLIENT-2 | A client may request only its own scopes (otherwise 400 `invalid_scope`); without `scope` it gets all of them. The response lists the granted scopes. |
| R-CLIENT-3 | A client token is valid only for the services of its group (token audience): **Catalog** = ProductService, **Orders** = OrderService plus the catalog, because OrderService passes the caller's token on to ProductService when it checks the ordered products. A token of another group is rejected with 401. |
| R-CLIENT-4 | Deleting a product needs the `Admin` role or the `catalog.admin` scope: a client without it gets 403 and the product is unchanged. Other operations do not check scopes *(demo simplification)*. |
| R-CLIENT-5 | Clients of the demo: `catalog-api-tests` (`catalog.read catalog.write catalog.admin`, audience `catalog`) and `orders-api-tests` (`orders.read orders.write catalog.read`, audiences `orders`, `catalog`). |

## Catalog — ProductService

| ID | Rule |
|----|------|
| R-PROD-1 | Any authenticated user may create and update products. Only `Admin` may delete them: other users get 403 and the product is unchanged; anonymous callers get 401. Machine clients: R-CLIENT-4. |
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
