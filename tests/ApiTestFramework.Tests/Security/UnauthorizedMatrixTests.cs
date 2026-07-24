using System.Net;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.DI;
using ApiTestFramework.OpenApi.Loader;
using ApiTestFramework.OpenApi.Models;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Tests.Security;

/// <summary>
/// Authorization matrix generated from the swagger specs: every endpoint that
/// declares Bearer security must reject an unauthenticated request with 401.
/// A newly added protected endpoint gets its 401 test automatically; an
/// endpoint that silently loses its security requirement drops out of this
/// matrix and shows up in the swagger drift check instead.
/// </summary>
[TestFixture]
[AllureSuite("Security")]
[AllureFeature("Authorization matrix")]
public class UnauthorizedMatrixTests : BaseTest
{
    private static readonly (string Service, string SwaggerFile)[] Services =
    {
        ("AuthService", "auth-swagger.json"),
        ("ProductService", "product-swagger.json"),
        ("OrderService", "order-swagger.json")
    };

    private static IEnumerable<TestCaseData> ProtectedEndpoints()
    {
        foreach (var (service, swaggerFile) in Services)
        {
            var swaggerPath = Path.Combine(AppContext.BaseDirectory, "swagger", swaggerFile);
            var document = OpenApiSpecLoader.LoadFromFile(swaggerPath);

            foreach (var endpoint in OpenApiSpecLoader.GetEndpoints(document).Where(e => e.RequiresAuth))
            {
                yield return new TestCaseData(service, endpoint)
                    .SetName($"{service}_{endpoint.OperationId}_WithoutToken_ReturnsUnauthorized")
                    .SetDescription($"{endpoint.Method} {endpoint.Path} without a Bearer token");
            }
        }
    }

    [TestCaseSource(nameof(ProtectedEndpoints))]
    [AllureSeverity(SeverityLevel.critical)]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized(
        string service, EndpointDefinition endpoint)
    {
        var client = ContainerProvider.ResolveNamed<ApiClient>(service);

        var builder = RequestBuilder.Create()
            .WithMethod(Enum.Parse<Method>(endpoint.Method, ignoreCase: true))
            .WithPath(FillPathParameters(endpoint.Path))
            .WithHeader("Authorization", ""); // suppress auto-injected token

        if (endpoint.Method is "POST" or "PUT")
            builder.WithRawJsonBody("{}"); // auth is checked before the body

        var response = await client.SendAsync(builder);

        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
    }

    /// <summary>Replaces {placeholders} with values that satisfy route constraints —
    /// authentication is rejected before any of them are looked up.</summary>
    private static string FillPathParameters(string path)
    {
        var result = path;
        while (true)
        {
            var start = result.IndexOf('{');
            if (start < 0) return result;
            var end = result.IndexOf('}', start);
            var name = result[(start + 1)..end].ToLowerInvariant();

            var sample = name.Contains("email") ? "nobody@example.com"
                : name.Contains("id") ? "999999"
                : "sample";

            result = result[..start] + sample + result[(end + 1)..];
        }
    }
}
