using Bogus;
using ApiTestFramework.Clients.OrderService;
using ApiTestFramework.Steps.Builders.OrderService;

namespace ApiTestFramework.Steps.DataGenerators;

/// <summary>
/// Facade over the generated OrderService builders
/// (<see cref="CreateOrderRequestBuilder"/> et al.).
/// Prefer using the builders directly in tests when you need to customize
/// nested DTOs; these helpers cover the common cases.
/// </summary>
public static class OrderDataGenerator
{
    private static readonly Faker _faker = new();

    /// <summary>
    /// Generates a valid single-line CreateOrderRequest (nested customer with
    /// address + shipping filled by builder defaults). Requires a valid productId.
    /// </summary>
    public static CreateOrderRequest GenerateCreateOrderRequest(int productId)
        => new CreateOrderRequestBuilder()
            .ForProduct(productId, _faker.Random.Int(1, 5))
            .Build();

    /// <summary>
    /// Generates a CreateOrderRequest with builder-level overrides, e.g.
    /// <c>GenerateCreateOrderRequest(id, b => b.WithCustomer(c => c.WithEmail("x@y.z")))</c>.
    /// </summary>
    public static CreateOrderRequest GenerateCreateOrderRequest(
        int productId, Action<CreateOrderRequestBuilder> customize)
    {
        var builder = new CreateOrderRequestBuilder()
            .ForProduct(productId, _faker.Random.Int(1, 5));
        customize(builder);
        return builder.Build();
    }

    /// <summary>
    /// Generates a multi-item CreateOrderRequest — one line per product.
    /// </summary>
    public static CreateOrderRequest GenerateMultiItemOrderRequest(params int[] productIds)
    {
        var builder = new CreateOrderRequestBuilder();
        foreach (var productId in productIds)
            builder.ForProduct(productId, _faker.Random.Int(1, 5));
        return builder.Build();
    }

    /// <summary>
    /// Generates an UpdateOrderRequest with a random status and refreshed
    /// nested customer data (name, email, address).
    /// </summary>
    public static UpdateOrderRequest GenerateUpdateOrderRequest()
        => new UpdateOrderRequestBuilder()
            .WithStatus(_faker.PickRandom<OrderStatus>())
            .WithCustomer(c => c.WithAddress(a => { }))
            .Build();
}
