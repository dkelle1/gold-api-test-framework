using System.Net;
using System.Text;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Tests.ImportService;

[TestFixture]
[AllureSuite("ImportService")]
[AllureFeature("Batch Product CSV Import")]
public class ImportBatchTests : BaseTest
{
    private ImportServiceSteps _steps = null!;

    protected override void OnFixtureSetUp()
    {
        _steps = new ImportServiceSteps();
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a batch CSV import with 1000 products completes successfully")]
    public async Task ImportProductsCsv_With1000Rows_CompletesSuccessfully()
    {
        var csv = BuildCsv(1000);

        var startResponse = await _steps.StartCsvImportAsync("products-1000.csv", csv);
        startResponse.ShouldHaveStatusCode(HttpStatusCode.Accepted);

        var accepted = startResponse.ShouldHaveData();
        var status = await _steps.WaitForImportCompletionAsync(accepted.BatchId, TimeSpan.FromSeconds(120));

        status.Batch.Status.Should().Be("Completed");
        status.Progress.TotalRecords.Should().Be(1000);
        status.Progress.SucceededRecords.Should().Be(1000);
        status.Progress.FailedRecords.Should().Be(0);
        status.Audit.CompletedAt.Should().NotBeNull();
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that invalid rows are reported in batch progress")]
    public async Task ImportProductsCsv_WithInvalidRows_TracksFailures()
    {
        var csv = new StringBuilder()
            .AppendLine("Name,Description,Price,StockQuantity,Category")
            .AppendLine("Valid Product,Valid Description,19.99,12,Test")
            .AppendLine("Invalid Product,Invalid Description,NOT_A_NUMBER,5,Test")
            .ToString();

        var startResponse = await _steps.StartCsvImportAsync("products-invalid.csv", csv);
        startResponse.ShouldHaveStatusCode(HttpStatusCode.Accepted);

        var accepted = startResponse.ShouldHaveData();
        var status = await _steps.WaitForImportCompletionAsync(accepted.BatchId);

        status.Batch.Status.Should().Be("Completed");
        status.Progress.TotalRecords.Should().Be(2);
        status.Progress.SucceededRecords.Should().Be(1);
        status.Progress.FailedRecords.Should().Be(1);
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("Verify that unknown batch id returns 404")]
    public async Task GetImportStatus_WithUnknownBatchId_ReturnsNotFound()
    {
        var response = await _steps.GetImportStatusAsync(Guid.NewGuid().ToString());

        response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    private static string BuildCsv(int rowCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Description,Price,StockQuantity,Category");

        for (var i = 1; i <= rowCount; i++)
        {
            sb.AppendLine($"Product {i},Description {i},9.99,{10 + (i % 50)},Category{i % 10}");
        }

        return sb.ToString();
    }
}
