using ApiTestFramework.OpenApi.Validation;
using FluentAssertions;
using Microsoft.OpenApi.Models;
using NUnit.Framework;

namespace ApiTestFramework.OpenApi.Tests;

[TestFixture]
public class SwaggerDriftCheckerTests
{
    [Test]
    public void IdenticalDocumentsReportNoDrift()
    {
        var report = SwaggerDriftChecker.Compare(
            FixtureLoader.LoadWidgetSwagger(),
            FixtureLoader.LoadWidgetSwagger());

        report.HasDrift.Should().BeFalse();
        report.Drift.Should().BeEmpty();
        report.Warnings.Should().BeEmpty();
    }

    [Test]
    public void RemovedEndpointIsDrift()
    {
        var live = FixtureLoader.LoadWidgetSwagger();
        live.Paths.Remove("/api/widgets/{id}");

        var report = SwaggerDriftChecker.Compare(FixtureLoader.LoadWidgetSwagger(), live);

        report.HasDrift.Should().BeTrue();
        report.Drift.Should().ContainSingle(d => d.Contains("GET /api/widgets/{id}"));
    }

    [Test]
    public void RenamedPropertyIsDriftInBothDirections()
    {
        var live = FixtureLoader.LoadWidgetSwagger();
        var props = live.Components.Schemas["PartRequest"].Properties;
        props["count"] = props["quantity"];
        props.Remove("quantity");

        var report = SwaggerDriftChecker.Compare(FixtureLoader.LoadWidgetSwagger(), live);

        report.Drift.Should().Contain(d => d.Contains("'quantity' removed"));
        report.Drift.Should().Contain(d => d.Contains("new property 'count'"));
    }

    [Test]
    public void ChangedPropertyTypeIsDrift()
    {
        var live = FixtureLoader.LoadWidgetSwagger();
        live.Components.Schemas["CreateWidgetRequest"].Properties["name"].Type = "integer";

        var report = SwaggerDriftChecker.Compare(FixtureLoader.LoadWidgetSwagger(), live);

        report.Drift.Should().ContainSingle(d =>
            d.Contains("CreateWidgetRequest.name") && d.Contains("type changed string -> int"));
    }

    [Test]
    public void RequiredFlagChangeIsWarningNotDrift()
    {
        var live = FixtureLoader.LoadWidgetSwagger();
        live.Components.Schemas["CreateWidgetRequest"].Required.Remove("name");

        var report = SwaggerDriftChecker.Compare(FixtureLoader.LoadWidgetSwagger(), live);

        report.HasDrift.Should().BeFalse();
        report.Warnings.Should().ContainSingle(w =>
            w.Contains("CreateWidgetRequest.name") && w.Contains("required"));
    }

    [Test]
    public void EnumValueCountChangeIsDrift()
    {
        var live = FixtureLoader.LoadWidgetSwagger();
        live.Components.Schemas["WidgetStatus"].Enum.RemoveAt(0);

        var report = SwaggerDriftChecker.Compare(FixtureLoader.LoadWidgetSwagger(), live);

        report.Drift.Should().ContainSingle(d =>
            d.Contains("WidgetStatus") && d.Contains("enum value count changed"));
    }
}
