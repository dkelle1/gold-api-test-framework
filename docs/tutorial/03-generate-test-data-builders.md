# Chapter 3 — Generating test-data builders

Request bodies of real APIs are nested: an order carries a customer with an address, a list of
items and shipping details. Building them by hand in every test is noisy and breaks whenever the
contract changes. The framework generates a fluent builder per request schema; you add domain
knowledge in a separate partial class that regeneration never touches.

In this chapter:

- [Generating builders from the contract](#generating-builders-from-the-contract)
- [Building nested requests](#building-nested-requests)
- [Customizing builders with partial classes](#customizing-builders-with-partial-classes)
- [Exposing builders through data generators](#exposing-builders-through-data-generators)

---

## Generating builders from the contract

### Getting ready

The service is registered in `generator.json` and its DTOs exist ([chapter 2](02-onboard-a-service.md)).

### How to do it…

1. Run the generator:

   ```powershell
   ./examples/demo-shop/api-tests/scripts/regenerate-all.ps1
   ```

   or directly:

   ```bash
   dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config examples/demo-shop/api-tests/generator.json
   ```

2. Look at the result: `DemoShop.Steps/Builders/OrderService/Generated/` now holds
   `CreateOrderRequestBuilder.g.cs`, `CustomerRequestBuilder.g.cs`, `AddressRequestBuilder.g.cs`,
   `OrderItemRequestBuilder.g.cs`, `ShippingRequestBuilder.g.cs`, `UpdateOrderRequestBuilder.g.cs`.

3. Commit the generated files together with the contract change that caused them.

### How it works…

`SchemaExtractor` walks every request body **transitively** — nested objects, arrays and nullable
`oneOf` wrappers — and `BuilderScaffolder` emits one `partial` class per object schema. Defaults
come from Bogus, chosen by property name and format (an `email` gets an e-mail, `city` a city,
`quantity` 1–5, enums a random value). **Ids get no default**: a builder never invents a foreign key
such as `productId` — the test must supply a real one.

### There's more…

- Output is deterministic; CI regenerates it and fails if the committed files differ.
- Removed schemas disappear: the generator deletes stale `*.g.cs` in its output folders first.

---

## Building nested requests

### How to do it…

```csharp
var request = new CreateOrderRequestBuilder()                       // valid defaults everywhere
    .WithCustomer(c => c
        .WithName("Jane Doe")
        .WithAddress(a => a.WithCity("Kraków")))                   // nested builder, two levels down
    .AddItem(i => i.WithProductId(productA.Id).WithQuantity(2))     // collection element via its builder
    .AddItem(i => i.WithProductId(productB.Id).WithQuantity(1))
    .WithShipping(s => s.WithMethod(ShippingMethod.Express))
    .Build();
```

For negative tests, mutate anything with the escape hatch:

```csharp
var invalid = new CreateOrderRequestBuilder()
    .ForProduct(product.Id)
    .With(r => r.Customer.Email = "")            // what the fluent API deliberately doesn't offer
    .Build();
```

### How it works…

Each nested `With…(Action<…Builder>)` starts from that builder's defaults, so you only state what
the test is about — the rest stays valid. Collections start empty; `Add…`, `Clear…` and
`With…(List<>)` manage them.

---

## Customizing builders with partial classes

### Getting ready

Find a rule the contract cannot express. The demo shop has two:
a password policy, and "an order is about products".

### How to do it…

1. Create a file with the **same class name** in `DemoShop.Steps/Builders/Custom/` — never edit `*.g.cs`.

2. Use the hooks and add domain verbs. From
   [`Custom/CreateOrderRequestBuilder.cs`](../../examples/demo-shop/api-tests/DemoShop.Steps/Builders/Custom/CreateOrderRequestBuilder.cs):

   ```csharp
   public partial class CreateOrderRequestBuilder
   {
       // richer defaults: a realistic order has a customer address and explicit shipping
       partial void OnDefaultsApplied()
       {
           _instance.Customer = new CustomerRequestBuilder().WithAddress(a => { }).Build();
           _instance.Shipping = new ShippingRequestBuilder().Build();
       }

       // the one thing a builder cannot invent is a valid ProductId
       public CreateOrderRequestBuilder ForProduct(int productId, int quantity = 1)
           => AddItem(i => i.WithProductId(productId).WithQuantity(quantity));

       public CreateOrderRequestBuilder ForProducts(params (int ProductId, int Quantity)[] lines)
       {
           foreach (var (productId, quantity) in lines) ForProduct(productId, quantity);
           return this;
       }
   }
   ```

   and from [`Custom/RegisterRequestBuilder.cs`](../../examples/demo-shop/api-tests/DemoShop.Steps/Builders/Custom/RegisterRequestBuilder.cs):

   ```csharp
   partial void OnBeforeBuild(RegisterRequest instance)   // runs on every Build()
   {
       if (string.IsNullOrWhiteSpace(instance.Password) || instance.Password.Length < 8)
           instance.Password = "TestPass123!";
   }
   ```

3. Regenerate — your file is untouched, the generated half is refreshed.

### How it works…

The generated constructor calls `ApplyDefaults()` and then `OnDefaultsApplied()`; `Build()` calls
`OnBeforeBuild(instance)`. Both are C# `partial void` methods: they cost nothing until you
implement them, and the compiler joins the two halves of the class.

---

## Exposing builders through data generators

### How to do it…

Give tests one-line entry points for common shapes
([`DataGenerators/OrderDataGenerator.cs`](../../examples/demo-shop/api-tests/DemoShop.Steps/DataGenerators/OrderDataGenerator.cs)):

```csharp
public static CreateOrderRequest GenerateCreateOrderRequest(int productId)
    => new CreateOrderRequestBuilder().ForProduct(productId, _faker.Random.Int(1, 5)).Build();

public static CreateOrderRequest GenerateMultiItemOrderRequest(params int[] productIds) { ... }
```

### There's more…

Use data generators for "a valid X", builders when the test is *about* specific fields. Both are
plain code in the Steps project, shared by API tests and e2e flows.

## See also

- [Generators reference](../framework/generators.md)
- [Chapter 4 — Writing steps](04-write-steps.md)
