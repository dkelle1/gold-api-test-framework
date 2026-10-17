# Demo shop — the example system under test

> **This folder is an example, not part of the framework.**
> It exists so that every feature of the framework can be shown — and tested in CI —
> against a realistic set of APIs. Delete it when you adopt the framework for your
> own product ([tutorial chapter 10](../../docs/tutorial/10-adopt-for-your-product.md)).

```
examples/demo-shop/
├── services/                 # the system under test (3 × ASP.NET Core minimal APIs)
│   ├── AuthService/          #   :5300  register / login → JWT
│   ├── ProductService/       #   :5100  product catalog (JWT, delete = Admin only)
│   └── OrderService/         #   :5200  orders, validates products via ProductService
├── docker-compose.yml        # SQL Server + Redis + the three services
├── api-tests/                # how a product team uses the framework
│   ├── DemoShop.Clients/     #   swagger contracts, DTOs, route constants
│   ├── DemoShop.Steps/       #   steps, generated builders (+ custom partials), data generators
│   ├── DemoShop.Tests/       #   NUnit suites, end-to-end flows, generated scaffolds
│   ├── generator.json        #   config for ApiTestFramework.Generator.Cli
│   ├── openapi-tools.json    #   config for the Python tools in /tools
│   └── scripts/              #   refresh-swagger.ps1, regenerate-all.ps1
├── collections/              # generated Postman + Bruno collections
└── perf/k6/                  # generated k6 load-test scripts
```

The services only depend on each other over HTTP and do not reference the framework
or the tests in any way. The API tests depend on the framework, never the other way round.

## Run it

```bash
docker compose -f examples/demo-shop/docker-compose.yml up -d --build --wait
dotnet test examples/demo-shop/api-tests/DemoShop.Tests
docker compose -f examples/demo-shop/docker-compose.yml down -v
```

Swagger UI: <http://localhost:5300>, <http://localhost:5100>, <http://localhost:5200>.

Without Docker you can run each service with `dotnet run --project examples/demo-shop/services/<Service>`,
but they need SQL Server (`localhost:1433`) and Redis (`localhost:6379`).

## The APIs

### AuthService — `:5300` (anonymous)

| Endpoint | Success | Errors |
|----------|---------|--------|
| `POST /api/auth/register` | 201 `AuthResponse` | 400 missing field / password < 6 chars, 409 username or email taken |
| `POST /api/auth/login`    | 200 `AuthResponse` | 400 missing field, 401 wrong credentials |

`AuthResponse { Token { AccessToken, TokenType, ExpiresAt, ExpiresInSeconds }, User { Username, Email, Role } }`.
Tokens are HS256 JWTs carrying the `role` claim, valid for 60 minutes.

### ProductService — `:5100` (Bearer token required)

| Endpoint | Success | Errors |
|----------|---------|--------|
| `GET /api/products` | 200 active products | 401 |
| `GET /api/products/{id}` | 200 | 401, 404 |
| `GET /api/products/category/{category}` | 200 active products of that exact category | 401 |
| `POST /api/products` | 201 | 400 missing name / price ≤ 0, 401 |
| `PUT /api/products/{id}` | 200 (partial update, incl. `isActive`) | 401, 404 |
| `DELETE /api/products/{id}` | 204 — **role `Admin` only** | 401, 403, 404 |

`Product { Id, Name, Description, Price { Amount, Currency }, Inventory { StockQuantity, InStock }, Category, IsActive, Audit { CreatedAt, UpdatedAt } }`.

### OrderService — `:5200` (Bearer token required)

| Endpoint | Success | Errors |
|----------|---------|--------|
| `GET /api/orders` | 200 | 401 |
| `GET /api/orders/{id}` | 200 | 401, 404 |
| `GET /api/orders/customer/{email}` | 200 orders of that customer | 401 |
| `POST /api/orders` | 201 | 400 (see below), 401 |
| `PUT /api/orders/{id}` | 200 (partial: status / customer / shipping) | 401, 404 |
| `DELETE /api/orders/{id}` | 204 | 401, 404 |

A create request is **nested** — one request carries several DTO levels:

```
CreateOrderRequest { Customer { Name, Email, Address? }, Items [ { ProductId, Quantity } ], Shipping? { Method, Address?, Notes? } }
```

On create, OrderService calls ProductService **with the caller's token** for every product:
unknown product, inactive product, or insufficient stock (summed per product) → 400.
Shipping address falls back to the customer address.
`OrderStatus` and `ShippingMethod` are sent as **integers** (`Pending = 0`, `Standard = 0`, ...).

## Deliberate simplifications (it is a demo)

These are known and intentional — useful as test targets, not as production patterns:

- **Anyone can register as `Admin`** — the role comes from the register request, so test suites
  can provision admin identities without a seeded account.
- Passwords are hashed with unsalted SHA-256; the JWT signing key and SQL password live in
  `appsettings.json` / `docker-compose.yml`.
- Placing an order checks stock but **does not decrement it**.
- Orders have no owner: any authenticated user can read or change any order.
- Databases are created with `EnsureCreated()` (no migrations); Redis caches user and order lookups.
- Health checks only test that the port is open; there are no `/health` endpoints.

## Business rules and flows

- [`api-tests/domain-rules.md`](api-tests/domain-rules.md) — the intended behaviour as numbered rules.
- [`api-tests/flows/`](api-tests/flows/README.md) — end-to-end flows described in plain language; the
  tests in `DemoShop.Tests/EndToEnd/` are generated from them and link back.

## Where the tests are explained

- [Tutorial](../../docs/tutorial/README.md) — builds these tests step by step, A to Z.
- [`api-tests/DemoShop.Tests/EndToEnd`](api-tests/DemoShop.Tests/EndToEnd) — end-to-end flows across all services.
- [`api-tests/DemoShop.Tests/ProductService/ProductCategoryTests.cs`](api-tests/DemoShop.Tests/ProductService/ProductCategoryTests.cs) — a generated scaffold promoted to real tests.
