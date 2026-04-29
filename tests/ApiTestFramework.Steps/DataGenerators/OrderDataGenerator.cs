using Bogus;
using ApiTestFramework.Clients.OrderService;

namespace ApiTestFramework.Steps.DataGenerators;

/// <summary>
/// Bogus-based fake data generators for OrderService DTOs.
/// </summary>
public static class OrderDataGenerator
{
    private static readonly Faker _faker = new();

    /// <summary>
    /// Generates a valid CreateOrderRequest. Requires a valid productId.
    /// </summary>
    public static CreateOrderRequest GenerateCreateOrderRequest(int productId)
    {
        return new CreateOrderRequest
        {
            ProductId = productId,
            CustomerName = _faker.Name.FullName(),
            CustomerEmail = _faker.Internet.Email(),
            Quantity = _faker.Random.Int(1, 10)
        };
    }

    /// <summary>
    /// Generates a CreateOrderRequest with specific overrides.
    /// </summary>
    public static CreateOrderRequest GenerateCreateOrderRequest(int productId, Action<CreateOrderRequest> customize)
    {
        var request = GenerateCreateOrderRequest(productId);
        customize(request);
        return request;
    }

    /// <summary>
    /// Generates an UpdateOrderRequest with random data.
    /// </summary>
    public static UpdateOrderRequest GenerateUpdateOrderRequest()
    {
        return new UpdateOrderRequest
        {
            Quantity = _faker.Random.Int(1, 20),
            Status = _faker.PickRandom<OrderStatus>(),
            CustomerName = _faker.Name.FullName(),
            CustomerEmail = _faker.Internet.Email()
        };
    }
}
