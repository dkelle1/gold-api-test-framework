using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.DI;
using ApiTestFramework.OpenApi.Loader;
using ApiTestFramework.OpenApi.Models;
using ApiTestFramework.OpenApi.Validation;
using ApiTestFramework.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Tests.Contracts;

/// <summary>
/// Contract tests: the raw JSON the services return must conform to the
/// committed swagger response schemas. Typed deserialization would silently
/// ignore extra/renamed fields — <see cref="JsonSchemaValidator"/> does not.
/// Together with the swagger drift gate this closes the loop: the spec matches
/// the service (drift check) and the wire format matches the spec (these tests).
/// </summary>
[TestFixture]
[AllureSuite("Contracts")]
[AllureFeature("Response schema conformance")]
public class ResponseContractTests : BaseTest
{
    private OrderServiceSteps _orderSteps = null!;
    private ProductServiceSteps _productSteps = null!;

    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _orderSteps = new OrderServiceSteps();
        _productSteps = new ProductServiceSteps();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("GET /api/orders/{id} response conforms to the Order schema (nested customer, items, shipping)")]
    public async Task OrderResponse_ConformsToSwaggerSchema()
    {
        var (order, _) = await _orderSteps.CreateOrderWithProductAsync();

        var json = await GetRawAsync("OrderService", $"/api/orders/{order.Id}");

        Validate(json, "Order", "order-swagger.json");
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("GET /api/products/{id} response conforms to the Product schema (nested price, inventory, audit)")]
    public async Task ProductResponse_ConformsToSwaggerSchema()
    {
        var product = await _productSteps.CreateProductAsync();

        var json = await GetRawAsync("ProductService", $"/api/products/{product.Id}");

        Validate(json, "Product", "product-swagger.json");
    }

    private static async Task<string> GetRawAsync(string service, string path)
    {
        var client = ContainerProvider.ResolveNamed<ApiClient>(service);
        var response = await client.SendAsync(
            RequestBuilder.Create().WithMethod(Method.Get).WithPath(path));

        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        return response.Content!;
    }

    private static void Validate(string json, string schemaName, string swaggerFile)
    {
        var violations = JsonSchemaValidator.Validate(json, schemaName, SchemasOf(swaggerFile));
        violations.Should().BeEmpty(
            $"the live response must conform to schema '{schemaName}' from {swaggerFile}");
    }

    private static readonly Dictionary<string, IReadOnlyList<SchemaDefinition>> SchemaCache = new();

    private static IReadOnlyList<SchemaDefinition> SchemasOf(string swaggerFile)
    {
        lock (SchemaCache)
        {
            if (!SchemaCache.TryGetValue(swaggerFile, out var schemas))
            {
                var document = OpenApiSpecLoader.LoadFromFile(
                    Path.Combine(AppContext.BaseDirectory, "swagger", swaggerFile));
                schemas = SchemaExtractor.GetAllSchemas(document);
                SchemaCache[swaggerFile] = schemas;
            }
            return schemas;
        }
    }
}
