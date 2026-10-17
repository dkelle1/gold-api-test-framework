# Chapter 4 — Writing steps

A **step** is one business operation against one service: "create a product", "get the orders of
a customer". Tests and e2e flows are sequences of steps. Steps are the only place that knows
routes, HTTP methods and how to authenticate — so they are also the only place to change when the
API changes.

In this chapter:

- [Creating a step class](#creating-a-step-class)
- [Adding a step for a new endpoint](#adding-a-step-for-a-new-endpoint)
- [Happy-path steps and raw steps](#happy-path-steps-and-raw-steps)
- [Steps that depend on other services](#steps-that-depend-on-other-services)

---

## Creating a step class

### Getting ready

The service is registered in `appsettings.test.json` and has DTOs and routes ([chapter 2](02-onboard-a-service.md)).

### How to do it…

Create `DemoShop.Steps/ServiceSteps/<Service>Steps.cs`. The skeleton of
[`ProductServiceSteps`](../../examples/demo-shop/api-tests/DemoShop.Steps/ServiceSteps/ProductServiceSteps.cs):

```csharp
public class ProductServiceSteps
{
    private readonly ApiClient _client;
    private readonly ILogger _logger;
    private readonly string? _identity;
    private readonly string? _bearerToken;

    public ProductServiceSteps() : this(identity: null, bearerToken: null) { }

    private ProductServiceSteps(string? identity, string? bearerToken)
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("ProductService");  // the Services key
        _logger = Log.ForContext<ProductServiceSteps>();
        _identity = identity;
        _bearerToken = bearerToken;
    }

    // copies of the steps bound to another identity / token (chapter 6)
    public ProductServiceSteps AsIdentity(string identity) => new(identity, bearerToken: null);
    public ProductServiceSteps WithToken(string bearerToken) => new(identity: null, bearerToken: bearerToken);

    private RequestBuilder Authorized(RequestBuilder builder) =>
        _bearerToken != null ? builder.WithBearerToken(_bearerToken)
        : _identity != null ? builder.AsIdentity(_identity)
        : builder;                                  // default: the provider's token
}
```

### How it works…

- `ResolveNamed<ApiClient>("ProductService")` returns the client for that `Services` entry.
- Every request goes through `Authorized(...)`, so binding an identity to a step instance changes
  all of its requests at once.
- `[AllureStep]` on each method makes it a step in the report; `{param}` placeholders in the title
  are filled with argument values.

---

## Adding a step for a new endpoint

The demo shop had no step for `GET /api/products/category/{category}`; this is the one that was added.

### How to do it…

1. Add the route constant (`ProductServiceRoutes.ByCategory`) if it is missing.
2. Add the method:

   ```csharp
   [AllureStep("Get products in category: {category}")]
   public async Task<RestResponse<List<Product>>> GetProductsByCategoryAsync(string category)
   {
       var response = await _client.SendAsync<List<Product>>(
           Authorized(RequestBuilder.Create()
               .WithMethod(Method.Get)
               .WithPath(ProductServiceRoutes.ByCategory)
               .WithPathSegment("category", category)));

       return response;
   }
   ```

3. Build. The step is now available to API tests ([chapter 5](05-scaffold-and-promote-tests.md))
   and e2e flows ([chapter 7](07-end-to-end-flows.md)) alike.

### How it works…

Read operations return the raw `RestResponse<T>`: the caller decides what "correct" means
(200 with data, 404, 403…), and the response object carries both status and body.

---

## Happy-path steps and raw steps

### How to do it…

Use two flavours for operations that create things:

```csharp
// happy path: asserts success and returns the entity — for Arrange sections and e2e flows
[AllureStep("Create an order")]
public async Task<Order> CreateOrderAsync(CreateOrderRequest createRequest)
{
    var response = await _client.SendAsync<Order>(
        Authorized(RequestFactory.Post(OrderServiceRoutes.Base, createRequest)));
    response.ShouldHaveStatusCode(HttpStatusCode.Created);
    return response.ShouldHaveData();
}

// raw: returns the response untouched — for negative tests
[AllureStep("Attempt to create an order (raw response)")]
public async Task<RestResponse<Order>> TryCreateOrderAsync(CreateOrderRequest createRequest)
    => await _client.SendAsync<Order>(
        Authorized(RequestFactory.Post(OrderServiceRoutes.Base, createRequest)));
```

### How it works…

A happy-path step failing means "the precondition could not be set up" — the report shows it at
the step, not as a confusing assertion later. The `Try…` naming makes it obvious in a test which
calls are expected to fail.

### There's more…

Add convenience steps for frequent setups, e.g. `CreateProductAndReturnIdAsync()`, or
`AuthServiceSteps.RegisterIdentityAsync(name, role)` which registers a user and stores its token.

---

## Steps that depend on other services

### How to do it…

When creating one resource needs another, let the step use the other service's steps — and pass
its identity binding along. From
[`OrderServiceSteps`](../../examples/demo-shop/api-tests/DemoShop.Steps/ServiceSteps/OrderServiceSteps.cs):

```csharp
var productSteps = new ProductServiceSteps();
_productSteps = bearerToken != null ? productSteps.WithToken(bearerToken)
    : identity != null ? productSteps.AsIdentity(identity)
    : productSteps;

[AllureStep("Create order with new product (full setup)")]
public async Task<(Order Order, int ProductId)> CreateOrderWithProductAsync()
{
    var productId = await _productSteps.CreateProductAndReturnIdAsync();
    var order = await CreateOrderAsync(OrderDataGenerator.GenerateCreateOrderRequest(productId));
    return (order, productId);
}
```

### There's more…

Keep steps free of test assertions beyond "the operation succeeded". Business expectations
belong in tests, so the same step can serve a positive test, a negative test and an e2e flow.

## See also

- [Core reference — building requests](../framework/core.md#building-and-sending-requests)
- [Authentication — binding an identity to a step class](../framework/authentication.md#binding-an-identity-to-a-step-class)
