using System.Text.Json.Nodes;
using ApiTestFramework.OpenApi.Generator;
using ApiTestFramework.OpenApi.Loader;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.OpenApi.Tests;

[TestFixture]
public class NegativeCaseGeneratorTests
{
    private IReadOnlyList<NegativeCase> _cases = null!;

    [OneTimeSetUp]
    public void Generate()
    {
        var document = FixtureLoader.LoadWidgetSwagger();
        var schemas = SchemaExtractor.GetRequestSchemas(document);
        _cases = NegativeCaseGenerator.ForSchema("CreateWidgetRequest", schemas);
    }

    [Test]
    public void GeneratesOneCasePerRequiredPropertyPlusEmptyArrayVariants()
    {
        // required: name, parts (+ empty variant), parts[0].partId, parts[0].quantity
        _cases.Select(c => c.Name).Should().BeEquivalentTo(
            "Missing_name",
            "Missing_parts",
            "Empty_parts",
            "Missing_parts[0]_partId",
            "Missing_parts[0]_quantity");
    }

    [Test]
    public void EveryPayloadIsValidJson()
    {
        foreach (var negativeCase in _cases)
        {
            var parse = () => JsonNode.Parse(negativeCase.JsonPayload);
            parse.Should().NotThrow($"payload of {negativeCase.Name} must be valid JSON");
        }
    }

    [Test]
    public void MissingTopLevelPropertyIsActuallyAbsent()
    {
        var payload = PayloadOf("Missing_name");
        payload.ContainsKey("name").Should().BeFalse();
        payload.ContainsKey("parts").Should().BeTrue("only one violation per payload");
    }

    [Test]
    public void MissingNestedArrayElementPropertyIsActuallyAbsent()
    {
        var payload = PayloadOf("Missing_parts[0]_partId");
        var part = payload["parts"]!.AsArray()[0]!.AsObject();
        part.ContainsKey("partId").Should().BeFalse();
        part.ContainsKey("quantity").Should().BeTrue();
    }

    [Test]
    public void EmptyArrayCaseKeepsThePropertyButEmptiesIt()
    {
        var payload = PayloadOf("Empty_parts");
        payload["parts"]!.AsArray().Should().BeEmpty();
    }

    [Test]
    public void BaselineOmitsOptionalNullableProperties()
    {
        var document = FixtureLoader.LoadWidgetSwagger();
        var schemas = SchemaExtractor.GetRequestSchemas(document);
        var baseline = NegativeCaseGenerator.BuildBaseline(
            schemas.Single(s => s.Name == "CreateWidgetRequest"),
            schemas.ToDictionary(s => s.Name));

        baseline.ContainsKey("details").Should().BeFalse("nullable optional properties are omitted");
        baseline.ContainsKey("name").Should().BeTrue();
        baseline["contactEmail"]!.GetValue<string>().Should().Contain("@");
        baseline["parts"]!.AsArray().Should().HaveCount(1);
    }

    [Test]
    public void UnknownSchemaThrows()
    {
        var document = FixtureLoader.LoadWidgetSwagger();
        var schemas = SchemaExtractor.GetRequestSchemas(document);
        var act = () => NegativeCaseGenerator.ForSchema("NoSuchSchema", schemas);
        act.Should().Throw<ArgumentException>();
    }

    private JsonObject PayloadOf(string name) =>
        JsonNode.Parse(_cases.Single(c => c.Name == name).JsonPayload)!.AsObject();
}
