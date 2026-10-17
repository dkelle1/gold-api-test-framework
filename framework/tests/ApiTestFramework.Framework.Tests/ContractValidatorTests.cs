using Allure.NUnit.Attributes;
using ApiTestFramework.Contracts;
using ApiTestFramework.Core.Base;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Framework.Tests;

/// <summary>
/// ContractValidator + JsonSchemaValidator against the widgets fixture contract — no services.
/// </summary>
[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Contract Validation")]
[Category("Unit")]
public class ContractValidatorTests : BaseTest
{
    private const string ValidWidget =
        """
        {"id":1,"name":"Bolt","status":2,"color":"red","price":9.5,"createdAt":"2026-01-01T12:00:00Z",
         "reference":"0d1d5e1c-0000-4000-8000-000000000001","owner":{"email":"a@b.c"},"note":null,
         "parts":[{"quantity":1},{"quantity":3}]}
        """;

    private ContractValidator _validator = null!;

    protected override void OnFixtureSetUp()
    {
        var spec = Path.Combine(AppContext.BaseDirectory, "Fixtures", "widgets-contract.json");
        _validator = new ContractValidator(new[] { OpenApiContract.Load("Widgets", spec) });
    }

    private ContractCheck Check(string method, string path, int status, string? body,
        string? template = null, string? service = "Widgets", string contentType = "application/json")
        => _validator.Check(service, method, path, template, status, contentType, body);

    // ── operation and status ───────────────────────────────────────────────

    [Test]
    public void ValidResponse_Conforms()
        => Check("GET", "/api/widgets/1", 200, ValidWidget).Violations.Should().BeEmpty();

    [Test]
    public void MostSpecificTemplate_WinsOverParameterTemplate()
    {
        var check = Check("GET", "/api/widgets/search/bolt", 200, $"[{ValidWidget}]");

        check.OperationTemplate.Should().Be("/api/widgets/search/{term}");
        check.IsValid.Should().BeTrue();
    }

    [Test]
    public void RouteTemplate_GivesExactMatch()
        => Check("GET", "/api/widgets/7", 200, ValidWidget, template: "/api/widgets/{id}")
            .OperationTemplate.Should().Be("/api/widgets/{id}");

    [Test]
    public void UndocumentedOperation_IsAViolation()
        => Check("PATCH", "/api/widgets/1", 200, ValidWidget).Violations.Should().ContainSingle()
            .Which.Should().Contain("PATCH /api/widgets/1 is not documented");

    [Test]
    public void UndocumentedStatus_IsAViolation_ListingDocumentedCodes()
        => Check("GET", "/api/widgets/1", 500, "{}").Violations.Should().ContainSingle()
            .Which.Should().Contain("status 500 is not documented").And.Contain("200, 404");

    [Test]
    public void StatusRangeAndDefault_AreHonoured()
    {
        Check("GET", "/api/ranged", 202, """{"message":"ok"}""").IsValid.Should().BeTrue();
        Check("GET", "/api/ranged", 503, null).IsValid.Should().BeTrue();
    }

    [Test]
    public void UnknownService_IsAViolation()
        => Check("GET", "/api/widgets", 200, "[]", service: "Billing").Violations
            .Should().ContainSingle().Which.Should().Contain("no contract is configured for service 'Billing'");

    [Test]
    public void WithoutService_ContractIsFoundByPath()
        => Check("GET", "/api/widgets/1", 200, ValidWidget, service: null).Contract.Should().Be("Widgets");

    // ── bodies ───────────────────────────────────────────────────────────────

    [Test]
    public void UndocumentedErrorBody_IsAViolation()
        => Check("GET", "/api/widgets", 401, """{"message":"nope"}""").Violations.Should().ContainSingle()
            .Which.Should().Contain("a response body is not documented for 401");

    [Test]
    public void EmptyBodyForDocumentedSchema_IsAViolation()
        => Check("GET", "/api/widgets/1", 200, "").Violations.Should().ContainSingle()
            .Which.Should().Contain("expected a JSON body (Widget)");

    [Test]
    public void EmptyBodyWithoutSchema_Conforms()
        => Check("DELETE", "/api/widgets/1", 204, null).IsValid.Should().BeTrue();

    [Test]
    public void InvalidJson_IsAViolation()
        => Check("GET", "/api/widgets/1", 200, "{not json").Violations.Should().ContainSingle()
            .Which.Should().Contain("body is not valid JSON");

    [Test]
    public void NonJsonContentType_IsAViolation()
        => Check("GET", "/api/widgets/1", 200, ValidWidget, contentType: "text/plain").Violations
            .Should().Contain(v => v.Contains("Content-Type 'text/plain' is not JSON"));

    // ── schema rules ─────────────────────────────────────────────────────────

    private IReadOnlyList<string> WidgetViolations(string json) => Check("GET", "/api/widgets/1", 200, json).Violations;

    [Test]
    public void IntegerEnum_RejectsStringNames()
        => WidgetViolations("""{"id":1,"name":"a","status":"Shipped","note":null}""")
            .Should().Contain(v => v.Contains("$.status") && v.Contains("not in enum [0, 1, 2]"));

    [Test]
    public void StringEnum_RejectsNumbers()
        => WidgetViolations("""{"id":1,"name":"a","color":0,"note":null}""")
            .Should().Contain(v => v.Contains("$.color") && v.Contains("not in enum ['red', 'blue']"));

    [Test]
    public void TypesAndFormats_AreChecked()
    {
        var violations = WidgetViolations(
            """{"id":1.5,"name":"a","price":"9","createdAt":"yesterday","reference":"x","owner":{"email":"nope"},"note":null}""");

        violations.Should().Contain(v => v.StartsWith("$.id: expected integer"));
        violations.Should().Contain(v => v.StartsWith("$.price: expected number, got string"));
        violations.Should().Contain(v => v.Contains("$.createdAt") && v.Contains("not a valid date-time"));
        violations.Should().Contain(v => v.Contains("$.reference") && v.Contains("not a valid uuid"));
        violations.Should().Contain(v => v.Contains("$.owner.email") && v.Contains("not a valid email"));
    }

    [Test]
    public void RangesAndLengths_AreChecked()
    {
        var violations = WidgetViolations(
            """{"id":1,"name":"a-name-that-is-way-too-long","price":-1,"parts":[{"quantity":1},{"quantity":0}],"note":null}""");

        violations.Should().Contain(v => v.StartsWith("$.name: longer than maxLength 20"));
        violations.Should().Contain(v => v.StartsWith("$.price: -1 is below the minimum 0"));
        violations.Should().Contain(v => v.StartsWith("$.parts[1].quantity: 0 is below the minimum 1"));
    }

    [Test]
    public void Nullability_IsRespected_IncludingNullableOneOfWrappers()
    {
        WidgetViolations(ValidWidget.Replace("""{"email":"a@b.c"}""", "null")).Should().BeEmpty();
        WidgetViolations("""{"id":1,"name":null,"note":null}""").Should().Contain("$.name: null is not allowed");
        WidgetViolations("""{"id":1,"name":"a","owner":{"email":5},"note":null}""")
            .Should().Contain(v => v.StartsWith("$.owner.email: expected string"));
    }

    [Test]
    public void StrictObjects_RejectUndeclaredAndMissingNonNullableProperties()
    {
        var violations = WidgetViolations("""{"id":1,"name":"a","unexpected":true}""");

        violations.Should().Contain("$.unexpected: property is not declared in the schema");
        violations.Should().Contain("$.price: non-nullable property is missing");
        violations.Should().NotContain(v => v.StartsWith("$.note"), "nullable properties may be absent");
    }

    [Test]
    public void LenientObjects_AllowUndeclaredAndMissingOptionalProperties()
    {
        var spec = Path.Combine(AppContext.BaseDirectory, "Fixtures", "widgets-contract.json");
        var lenient = new ContractValidator(new[] { OpenApiContract.Load("Widgets", spec) }, strictObjects: false);

        lenient.Check("Widgets", "GET", "/api/widgets/1", null, 200, "application/json", """{"id":1,"name":"a","extra":1}""")
            .IsValid.Should().BeTrue();
    }

    [Test]
    public void RequiredProperties_AreChecked()
        => WidgetViolations("""{"name":"a","note":null}""").Should().Contain("$.id: required property is missing");

    [Test]
    public void Discriminator_SelectsTheMappedSchema_AndAllOfIsMerged()
    {
        Check("GET", "/api/shapes/1", 200, """{"kind":"circle","radius":2}""").IsValid.Should().BeTrue();

        Check("GET", "/api/shapes/1", 200, """{"kind":"square","radius":2}""").Violations
            .Should().Contain("$.radius: property is not declared in the schema");
    }

    [Test]
    public void ViolationReport_NamesOperationContractAndEveryViolation()
    {
        var check = Check("GET", "/api/widgets/1", 200, """{"id":"1","name":"a","note":null}""", template: "/api/widgets/{id}");

        check.Describe().Should().StartWith("Contract violations for GET /api/widgets/{id} (/api/widgets/1) → 200 [Widgets]:")
            .And.Contain("$.id: expected integer, got string");
    }
}
