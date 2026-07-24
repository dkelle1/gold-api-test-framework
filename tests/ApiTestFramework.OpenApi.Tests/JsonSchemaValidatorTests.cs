using ApiTestFramework.OpenApi.Loader;
using ApiTestFramework.OpenApi.Models;
using ApiTestFramework.OpenApi.Validation;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.OpenApi.Tests;

[TestFixture]
public class JsonSchemaValidatorTests
{
    private IReadOnlyList<SchemaDefinition> _schemas = null!;

    [OneTimeSetUp]
    public void LoadFixture()
    {
        _schemas = SchemaExtractor.GetAllSchemas(FixtureLoader.LoadWidgetSwagger());
    }

    [Test]
    public void GetAllSchemasIncludesResponseSchemas()
    {
        _schemas.Select(s => s.Name).Should().Contain("WidgetResponse",
            "contract validation needs response schemas, not only request ones");
    }

    [Test]
    public void ConformingPayloadHasNoViolations()
    {
        var json = """
            {
              "name": "w1",
              "contactEmail": "a@b.c",
              "ownerId": 5,
              "price": 12.5,
              "active": true,
              "status": 1,
              "details": { "note": null, "city": "Gdansk" },
              "tags": [ "a", "b" ],
              "parts": [ { "partId": 1, "quantity": 2 } ]
            }
            """;

        JsonSchemaValidator.Validate(json, "CreateWidgetRequest", _schemas).Should().BeEmpty();
    }

    [Test]
    public void UndeclaredPropertyIsReported()
    {
        var json = """{ "id": 1, "name": "w", "surprise": true }""";

        JsonSchemaValidator.Validate(json, "WidgetResponse", _schemas)
            .Should().ContainSingle(v => v.Contains("$.surprise") && v.Contains("not declared"));
    }

    [Test]
    public void MissingNonNullablePropertyIsReported()
    {
        var json = """{ "id": 1 }""";

        JsonSchemaValidator.Validate(json, "WidgetResponse", _schemas)
            .Should().ContainSingle(v => v.Contains("$.name") && v.Contains("missing"));
    }

    [Test]
    public void NullOnNullablePropertyIsAllowed()
    {
        var json = """{ "note": null, "city": "X" }""";

        JsonSchemaValidator.Validate(json, "DetailsRequest", _schemas).Should().BeEmpty();
    }

    [Test]
    public void NullOnNonNullablePropertyIsReported()
    {
        var json = """{ "note": null, "city": null }""";

        JsonSchemaValidator.Validate(json, "DetailsRequest", _schemas)
            .Should().ContainSingle(v => v.Contains("$.city") && v.Contains("non-nullable"));
    }

    [Test]
    public void TypeMismatchIsReported()
    {
        var json = """{ "id": "not-a-number", "name": "w" }""";

        JsonSchemaValidator.Validate(json, "WidgetResponse", _schemas)
            .Should().ContainSingle(v => v.Contains("$.id") && v.Contains("expected int"));
    }

    [Test]
    public void ArrayElementViolationsCarryTheIndex()
    {
        var json = """
            {
              "name": "w",
              "parts": [ { "partId": 1, "quantity": 2 }, { "partId": "bad", "quantity": 2 } ]
            }
            """;

        JsonSchemaValidator.Validate(json, "CreateWidgetRequest", _schemas)
            .Should().ContainSingle(v => v.Contains("$.parts[1].partId"));
    }

    [Test]
    public void EnumAcceptsBothNumberAndString()
    {
        JsonSchemaValidator.Validate(WidgetWithStatus("2"), "CreateWidgetRequest", _schemas)
            .Should().BeEmpty();
        JsonSchemaValidator.Validate(WidgetWithStatus("\"Used\""), "CreateWidgetRequest", _schemas)
            .Should().BeEmpty();
        JsonSchemaValidator.Validate(WidgetWithStatus("true"), "CreateWidgetRequest", _schemas)
            .Should().ContainSingle(v => v.Contains("$.status") && v.Contains("expected enum"));
    }

    private static string WidgetWithStatus(string statusJson) => $$"""
        {
          "name": "w",
          "contactEmail": "a@b.c",
          "ownerId": 1,
          "price": 1.0,
          "active": true,
          "status": {{statusJson}},
          "tags": [],
          "parts": []
        }
        """;

    [Test]
    public void InvalidJsonIsASingleViolation()
    {
        JsonSchemaValidator.Validate("{ not json", "WidgetResponse", _schemas)
            .Should().ContainSingle(v => v.Contains("not valid JSON"));
    }
}
