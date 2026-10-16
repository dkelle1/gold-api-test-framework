using Bogus;
using ApiTestFramework.Clients.ProductService;
using ApiTestFramework.Steps.Builders.ProductService;

namespace ApiTestFramework.Steps.DataGenerators;

/// <summary>
/// Facade over the generated ProductService builders.
/// </summary>
public static class ProductDataGenerator
{
    /// <summary>
    /// Generates a valid CreateProductRequest with random data.
    /// </summary>
    public static CreateProductRequest GenerateCreateProductRequest()
        => new CreateProductRequestBuilder().Build();

    /// <summary>
    /// Generates multiple valid CreateProductRequests.
    /// </summary>
    public static List<CreateProductRequest> GenerateCreateProductRequests(int count)
        => Enumerable.Range(0, count)
            .Select(_ => new CreateProductRequestBuilder().Build())
            .ToList();

    /// <summary>
    /// Generates a CreateProductRequest with specific overrides.
    /// </summary>
    public static CreateProductRequest GenerateCreateProductRequest(Action<CreateProductRequest> customize)
    {
        var request = new CreateProductRequestBuilder().Build();
        customize(request);
        return request;
    }

    /// <summary>
    /// Generates an UpdateProductRequest with random data.
    /// UpdateProductRequest is all-optional, so the builder applies no defaults —
    /// randomization happens here explicitly.
    /// </summary>
    public static UpdateProductRequest GenerateUpdateProductRequest()
    {
        var faker = new Faker();
        return new UpdateProductRequestBuilder()
            .WithName(faker.Commerce.ProductName())
            .WithDescription(faker.Commerce.ProductDescription())
            .WithPrice(Math.Round(faker.Random.Decimal(1, 999), 2))
            .WithStockQuantity(faker.Random.Int(20, 500))
            .WithCategory(faker.Commerce.Categories(1).First())
            .Build();
    }
}
