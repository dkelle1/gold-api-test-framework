using ApiTestFramework.OpenApi.Loader;
using ApiTestFramework.OpenApi.Models;
using FluentAssertions;
using Microsoft.OpenApi.Models;
using NUnit.Framework;

namespace ApiTestFramework.OpenApi.Tests;

[TestFixture]
public class SchemaExtractorTests
{
    private IReadOnlyList<SchemaDefinition> _schemas = null!;

    [OneTimeSetUp]
    public void LoadFixture()
    {
        var document = FixtureLoader.LoadWidgetSwagger();
        _schemas = SchemaExtractor.GetRequestSchemas(document);
    }

    [Test]
    public void ClosureContainsAllRequestReachableSchemas()
    {
        _schemas.Select(s => s.Name).Should().BeEquivalentTo(
            "CreateWidgetRequest", "DetailsRequest", "PartRequest", "WidgetStatus");
    }

    [Test]
    public void ResponseOnlySchemasAreExcluded()
    {
        _schemas.Select(s => s.Name).Should().NotContain("WidgetResponse",
            "builders are only generated for request payloads");
    }

    [Test]
    public void EnumSchemaIsDetectedWithValues()
    {
        var status = _schemas.Single(s => s.Name == "WidgetStatus");
        status.IsEnum.Should().BeTrue();
        status.EnumValues.Should().BeEquivalentTo("New", "Used", "Refurbished");
    }

    [Test]
    public void NestedObjectBehindNullableOneOfIsResolved()
    {
        var details = Property("details");
        details.Kind.Should().Be(PropertyKind.Object);
        details.ClrType.Should().Be("DetailsRequest");
        details.RefSchema.Should().Be("DetailsRequest");
        details.Nullable.Should().BeTrue();
    }

    [Test]
    public void ArrayOfObjectsDescribesElementType()
    {
        var parts = Property("parts");
        parts.Kind.Should().Be(PropertyKind.Array);
        parts.ElementKind.Should().Be(PropertyKind.Object);
        parts.ClrType.Should().Be("PartRequest");
        parts.Required.Should().BeTrue();
    }

    [Test]
    public void ArrayOfPrimitivesDescribesElementType()
    {
        var tags = Property("tags");
        tags.Kind.Should().Be(PropertyKind.Array);
        tags.ElementKind.Should().Be(PropertyKind.Primitive);
        tags.ClrType.Should().Be("string");
    }

    [Test]
    public void EnumPropertyIsMappedToEnumKind()
    {
        var status = Property("status");
        status.Kind.Should().Be(PropertyKind.Enum);
        status.ClrType.Should().Be("WidgetStatus");
    }

    [Test]
    public void PrimitiveTypesAndFormatsAreMapped()
    {
        Property("name").ClrType.Should().Be("string");
        Property("contactEmail").Format.Should().Be("email");
        Property("ownerId").ClrType.Should().Be("int");
        Property("price").ClrType.Should().Be("decimal");
        Property("active").ClrType.Should().Be("bool");
    }

    [Test]
    public void RequiredFlagsFollowTheSchema()
    {
        Property("name").Required.Should().BeTrue();
        Property("parts").Required.Should().BeTrue();
        Property("ownerId").Required.Should().BeFalse();

        var part = _schemas.Single(s => s.Name == "PartRequest");
        part.Properties.Should().OnlyContain(p => p.Required);
    }

    [Test]
    public void PropertyNamesArePascalCased()
    {
        Property("contactEmail").Name.Should().Be("ContactEmail");
    }

    private SchemaPropertyDefinition Property(string jsonName) =>
        _schemas.Single(s => s.Name == "CreateWidgetRequest")
            .Properties.Single(p => p.JsonName == jsonName);
}

internal static class FixtureLoader
{
    public static OpenApiDocument LoadWidgetSwagger()
    {
        var baseDir = Path.GetDirectoryName(typeof(FixtureLoader).Assembly.Location)!;
        return OpenApiSpecLoader.LoadFromFile(Path.Combine(baseDir, "Fixtures", "widget-swagger.json"));
    }
}
