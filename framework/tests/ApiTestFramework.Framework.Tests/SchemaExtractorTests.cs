using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Base;
using ApiTestFramework.OpenApi.Loader;
using FluentAssertions;
using Microsoft.OpenApi.Readers;
using NUnit.Framework;

namespace ApiTestFramework.Framework.Tests;

[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Generators")]
[Category("Unit")]
public class SchemaExtractorTests : BaseTest
{
    [Test]
    public void RequestSchemas_ComeFromJsonBodiesOnly()
    {
        var document = new OpenApiStringReader().Read("""
            {
              "openapi": "3.0.1",
              "info": { "title": "t", "version": "1" },
              "paths": {
                "/widgets": { "post": {
                  "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/CreateWidget" } } } },
                  "responses": { "201": { "description": "Created" } } } },
                "/widgets/{id}": { "patch": {
                  "requestBody": { "content": { "application/merge-patch+json": { "schema": { "$ref": "#/components/schemas/PatchWidget" } } } },
                  "responses": { "200": { "description": "OK" } } } },
                "/connect/token": { "post": {
                  "requestBody": { "content": { "application/x-www-form-urlencoded": { "schema": { "$ref": "#/components/schemas/TokenRequest" } } } },
                  "responses": { "200": { "description": "OK" } } } }
              },
              "components": { "schemas": {
                "CreateWidget": { "type": "object", "properties": { "name": { "type": "string" } } },
                "PatchWidget": { "type": "object", "properties": { "name": { "type": "string" } } },
                "TokenRequest": { "type": "object", "properties": { "grant_type": { "type": "string" } } }
              } }
            }
            """, out var diagnostic);
        diagnostic.Errors.Should().BeEmpty();

        var names = SchemaExtractor.GetRequestSchemas(document).Select(s => s.Name);

        names.Should().BeEquivalentTo("CreateWidget", "PatchWidget");
    }
}
