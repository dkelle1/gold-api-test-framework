using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.DI;
using ApiTestFramework.OpenApi.Generator;
using ApiTestFramework.OpenApi.Loader;
using NUnit.Framework;

namespace ApiTestFramework.Tests.OrderService;

/// <summary>
/// Schema-driven negative tests: each case is derived from the request schema
/// in swagger/order-swagger.json by <see cref="NegativeCaseGenerator"/> —
/// one required-constraint violation per payload, expected 400.
/// New required fields in the contract automatically produce new test cases
/// after regeneration; no hand-written negative tests to keep in sync.
/// </summary>
[TestFixture]
[AllureSuite("OrderService")]
[AllureFeature("Schema-driven negative cases")]
public class OrderServiceNegativeTests : BaseTest
{
    private static IEnumerable<TestCaseData> CreateOrderNegativeCases()
    {
        var swaggerPath = Path.Combine(AppContext.BaseDirectory, "swagger", "order-swagger.json");
        var document = OpenApiSpecLoader.LoadFromFile(swaggerPath);
        var schemas = SchemaExtractor.GetRequestSchemas(document);

        foreach (var negativeCase in NegativeCaseGenerator.ForSchema("CreateOrderRequest", schemas))
        {
            yield return new TestCaseData(negativeCase)
                .SetName($"CreateOrder_{negativeCase.Name}_ReturnsBadRequest")
                .SetDescription(negativeCase.Description);
        }
    }

    [TestCaseSource(nameof(CreateOrderNegativeCases))]
    [AllureSeverity(SeverityLevel.normal)]
    public async Task CreateOrder_SchemaViolation_ReturnsBadRequest(NegativeCase negativeCase)
    {
        var client = ContainerProvider.ResolveNamed<ApiClient>("OrderService");

        var response = await client.SendAsync(
            RequestBuilder.Create()
                .WithMethod(RestSharp.Method.Post)
                .WithPath("/api/orders")
                .WithRawJsonBody(negativeCase.JsonPayload));

        response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
    }
}
