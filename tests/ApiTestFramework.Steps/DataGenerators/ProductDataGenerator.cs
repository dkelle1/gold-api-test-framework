using Bogus;
using ApiTestFramework.Clients.ProductService;

namespace ApiTestFramework.Steps.DataGenerators;

/// <summary>
/// Bogus-based fake data generators for ProductService DTOs.
/// </summary>
public static class ProductDataGenerator
{
    private static readonly Faker<CreateProductRequest> _createProductFaker = new Faker<CreateProductRequest>()
        .RuleFor(p => p.Name, f => f.Commerce.ProductName())
        .RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
        .RuleFor(p => p.Price, f => Math.Round(f.Random.Decimal(1, 999), 2))
        .RuleFor(p => p.StockQuantity, f => f.Random.Int(1, 500))
        .RuleFor(p => p.Category, f => f.Commerce.Categories(1).First());

    /// <summary>
    /// Generates a valid CreateProductRequest with random data.
    /// </summary>
    public static CreateProductRequest GenerateCreateProductRequest()
        => _createProductFaker.Generate();

    /// <summary>
    /// Generates multiple valid CreateProductRequests.
    /// </summary>
    public static List<CreateProductRequest> GenerateCreateProductRequests(int count)
        => _createProductFaker.Generate(count);

    /// <summary>
    /// Generates a CreateProductRequest with specific overrides.
    /// </summary>
    public static CreateProductRequest GenerateCreateProductRequest(Action<CreateProductRequest> customize)
    {
        var request = _createProductFaker.Generate();
        customize(request);
        return request;
    }

    /// <summary>
    /// Generates an UpdateProductRequest with random data.
    /// </summary>
    public static UpdateProductRequest GenerateUpdateProductRequest()
    {
        var faker = new Faker();
        return new UpdateProductRequest
        {
            Name = faker.Commerce.ProductName(),
            Description = faker.Commerce.ProductDescription(),
            Price = Math.Round(faker.Random.Decimal(1, 999), 2),
            StockQuantity = faker.Random.Int(1, 500),
            Category = faker.Commerce.Categories(1).First()
        };
    }
}
