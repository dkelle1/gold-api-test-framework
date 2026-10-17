# Chapter 2 — Onboarding a service

Everything the framework generates starts from one file: the service's OpenAPI contract. In this
chapter you put a service under test — the walkthrough uses the demo shop's **OrderService**, and
every step says what to do for a new service of your own (called *InventoryService* below).

In this chapter:

- [Committing the API contract](#committing-the-api-contract)
- [Getting DTOs](#getting-dtos)
- [Declaring routes](#declaring-routes)
- [Registering the service](#registering-the-service)

---

## Committing the API contract

### Getting ready

You need the service's OpenAPI 3 document — from a running service (`/swagger/v1/swagger.json`
for ASP.NET + Swashbuckle), from the API team, or written by hand.

### How to do it…

1. Save it next to the DTOs:
   `examples/demo-shop/api-tests/DemoShop.Clients/swagger/order-swagger.json`
   (for your service: `.../DemoShop.Clients/swagger/inventory-swagger.json`).

2. For a running service you can use the refresh script (add one line per service to its table):

   ```powershell
   ./examples/demo-shop/api-tests/scripts/refresh-swagger.ps1
   ```

3. **Review the file before committing.** Make sure it states:
   - `required` properties of request bodies,
   - every response code a caller can get, including `401` on secured operations and `403`
     where roles matter,
   - `nullable` only where the API really returns `null`,
   - enum representation exactly as on the wire,
   - error bodies (e.g. `{"message": "..."}` on 400/404/409) as a schema — contract validation
     rejects undocumented bodies.

### How it works…

The committed file is the **contract** every generator reads: builders, test scaffolds, Postman
collections, k6 scripts, the smoke runner and the mock. A generated framework is only as precise
as its contract — a response code that is not documented gets no scaffolded test.

> **Important:** runtime generators such as Swashbuckle are often *less* precise than a reviewed
> contract (no `required`, no 401s, everything nullable). The demo shop's contracts are curated,
> and CI uploads the live swagger as an artifact so you can compare. When refreshing, keep the
> curated parts.

### There's more…

The contract is also a test target in its own right. The smoke runner validates live responses
against it ([chapter 8](08-beyond-nunit.md)) — that is how CI found that order enums were
documented as strings while the service sends integers.

---

## Getting DTOs

### Getting ready

The contract is committed.

### How to do it…

Pick one approach per project:

- **Hand-written POCOs** (what the demo shop does): `DemoShop.Clients/Dtos/OrderDtos.cs` mirrors the
  schemas — nested types included (`Order`, `CustomerInfo`, `OrderItemInfo`, `CreateOrderRequest`, …)
  — in namespace `DemoShop.Clients.OrderService`.
- **NSwag**: copy `DemoShop.Clients/nswag-order.nswag`, point `url` at your swagger file, set
  `namespace` and `output`, then run `nswag run nswag-inventory.nswag /runtime:Net80`.

> **Important:** the demo's NSwag configs write `Generated/*.generated.cs` into the *same* namespaces
> as the hand-written DTOs. If you switch a service to NSwag, delete its hand-written DTO file,
> otherwise the types are defined twice.

### How it works…

The framework only needs classes that (de)serialize to your API's JSON (RestSharp uses
System.Text.Json with web defaults: camelCase, case-insensitive). Builders generated in chapter 3
refer to these classes **by schema name**, so DTO class names must match the schema names.

---

## Declaring routes

### How to do it…

Add one constants class per service to the Clients project
([`DemoShop.Clients/Routes.cs`](../../examples/demo-shop/api-tests/DemoShop.Clients/Routes.cs)):

```csharp
public static class OrderServiceRoutes
{
    public const string Base = "/api/orders";
    public const string ById = "/api/orders/{id}";
    public const string ByCustomerEmail = "/api/orders/customer/{email}";
}
```

### How it works…

Routes are templates: steps fill `{id}`-style placeholders with
`RequestBuilder.WithPathSegment("id", 42)`. When the API renames a route, one constant changes.

---

## Registering the service

### Getting ready

You know the service's name (used everywhere as a key), its local URL, and its swagger file name.

### How to do it…

Add the service to the three configuration files of the consumer project — no framework code
changes:

1. **Runtime** — `DemoShop.Tests/appsettings.test.json`:

   ```json
   "Services": {
     "OrderService":     { "BaseUrl": "http://localhost:5200" },
     "InventoryService": { "BaseUrl": "http://localhost:6100" }
   }
   ```

2. **C# generators** — `examples/demo-shop/api-tests/generator.json`:

   ```json
   "services": [
     { "name": "OrderService",     "swagger": "order-swagger.json" },
     { "name": "InventoryService", "swagger": "inventory-swagger.json" }
   ]
   ```

3. **Python tools** — `examples/demo-shop/api-tests/openapi-tools.json`:

   ```json
   { "key": "inventory", "name": "InventoryService", "swagger": "inventory-swagger.json",
     "baseUrl": "http://localhost:6100", "envVar": "INVENTORY_BASE_URL" }
   ```

4. Verify: `dotnet build` and `python tools/smoke_runner.py --list`.

### How it works…

At startup `ApiClientModule` registers a named `ApiClient` per `Services` entry, so steps can
resolve `ContainerProvider.ResolveNamed<ApiClient>("InventoryService")`. The generator and the
tools iterate their `services` lists. Each environment overrides URLs without editing JSON:
`TEST_Services__InventoryService__BaseUrl=https://inventory.staging.example.com`.

### There's more…

Keep the service **name** identical in all three files and in the swagger file name — it becomes
the generated namespaces (`DemoShop.Steps.Builders.InventoryService`) and fixture names
(`InventoryServiceGeneratedTests`).

## See also

- [Core reference — configuration](../framework/core.md#configuration)
- [Generators — `generator.json`](../framework/generators.md#generatorjson-reference)
- [Python tooling — `openapi-tools.json`](../framework/python-tooling.md#configuration--openapi-toolsjson)
