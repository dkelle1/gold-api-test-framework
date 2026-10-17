using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Framework.Tests;

/// <summary>
/// RequestBuilder → RestRequest translation (no HTTP involved).
/// </summary>
[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Request Builder")]
[Category("Unit")]
public class RequestBuilderTests : BaseTest
{
    [Test]
    public void PathSegmentsQueryAndHeaders_AreApplied()
    {
        var request = RequestBuilder.Create()
            .WithMethod(Method.Get)
            .WithPath("/api/orders/customer/{email}")
            .WithPathSegment("email", "jane@example.com")
            .WithQueryParameter("page", "2")
            .WithHeader("X-Correlation-Id", "abc")
            .WithTimeout(7)
            .Build();

        request.Method.Should().Be(Method.Get);
        request.Resource.Should().Be("/api/orders/customer/jane@example.com");
        Param(request, ParameterType.QueryString, "page").Should().Be("2");
        Param(request, ParameterType.HttpHeader, "X-Correlation-Id").Should().Be("abc");
        request.Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Test]
    public void Body_WithoutContentType_IsJson()
    {
        var request = RequestBuilder.Create()
            .WithMethod(Method.Post).WithPath("/x")
            .WithBody(new { Name = "a" })
            .Build();

        var body = request.Parameters.OfType<BodyParameter>().Single();
        body.ContentType.Value.Should().Be("application/json");
    }

    [Test]
    public void Body_WithContentType_UsesThatContentType_AndCamelCaseJson()
    {
        var request = RequestBuilder.Create()
            .WithMethod(Method.Patch).WithPath("/x")
            .WithBody(new { StockQuantity = 5 })
            .WithContentType("application/merge-patch+json")
            .Build();

        var body = request.Parameters.OfType<BodyParameter>().Single();
        body.ContentType.Value.Should().Be("application/merge-patch+json");
        body.Value.Should().Be("{\"stockQuantity\":5}");
    }

    [Test]
    public void StringBody_WithContentType_IsSentAsIs()
    {
        var request = RequestBuilder.Create()
            .WithMethod(Method.Post).WithPath("/x")
            .WithBody("Name,Price\nA,1")
            .WithContentType("text/csv")
            .Build();

        var body = request.Parameters.OfType<BodyParameter>().Single();
        body.ContentType.Value.Should().Be("text/csv");
        body.Value.Should().Be("Name,Price\nA,1");
    }

    private static string? Param(RestRequest request, ParameterType type, string name)
        => request.Parameters.FirstOrDefault(p => p.Type == type && p.Name == name)?.Value?.ToString();
}
