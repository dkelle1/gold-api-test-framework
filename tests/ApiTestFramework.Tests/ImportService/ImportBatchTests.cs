using System.Diagnostics;
using System.Net;
using System.Text;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.ImportService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Tests.ImportService;

[TestFixture]
[AllureSuite("ImportService")]
[AllureFeature("Batch Product CSV Import")]
public class ImportBatchTests : BaseTest
{
    private ApiClient _client = null!;

    protected override void OnFixtureSetUp()
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("ImportService");
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("Verify that a batch CSV import with 1000 products completes successfully")]
    public async Task ImportProductsCsv_With1000Rows_CompletesSuccessfully()
    {
        var csv = BuildCsv(1000);

        var startResponse = await StartCsvImportAsync("products-1000.csv", csv);
        startResponse.ShouldHaveStatusCode(HttpStatusCode.Accepted);

        var accepted = startResponse.ShouldHaveData();
        var status = await WaitForImportCompletionAsync(accepted.BatchId, TimeSpan.FromSeconds(120));

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

        var startResponse = await StartCsvImportAsync("products-invalid.csv", csv);
        startResponse.ShouldHaveStatusCode(HttpStatusCode.Accepted);

        var accepted = startResponse.ShouldHaveData();
        var status = await WaitForImportCompletionAsync(accepted.BatchId);

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
        var response = await GetImportStatusRawAsync(Guid.NewGuid().ToString());

        response.ShouldHaveStatusCode(HttpStatusCode.NotFound);
    }

    // --- helpers (multipart upload + polling not supported by generator) ---

    private async Task<RestResponse<ImportAcceptedResponse>> StartCsvImportAsync(string csvFileName, string csvContent)
    {
        var request = new RestRequest(ImportServiceRoutes.ProductCsvImport, Method.Post)
        {
            AlwaysMultipartFormData = true
        };
        request.AddFile("file", Encoding.UTF8.GetBytes(csvContent), csvFileName, "text/csv");
        return await _client.ExecuteAsync<ImportAcceptedResponse>(request);
    }

    private Task<RestResponse<ImportStatusResponse>> GetImportStatusRawAsync(string batchId)
        => _client.SendAsync<ImportStatusResponse>(
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(ImportServiceRoutes.StatusByBatchId)
                .WithPathSegment("batchId", batchId));

    private async Task<ImportStatusResponse> WaitForImportCompletionAsync(string batchId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(90);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < effectiveTimeout)
        {
            var response = await GetImportStatusRawAsync(batchId);
            response.ShouldHaveStatusCode(HttpStatusCode.OK);

            var status = response.ShouldHaveData();
            if (string.Equals(status.Batch.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status.Batch.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                return status;

            await Task.Delay(500);
        }

        throw new TimeoutException($"Import batch {batchId} did not complete within {effectiveTimeout.TotalSeconds}s.");
    }

    private static string BuildCsv(int rowCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Description,Price,StockQuantity,Category");
        for (var i = 1; i <= rowCount; i++)
            sb.AppendLine($"Product {i},Description {i},9.99,{10 + (i % 50)},Category{i % 10}");
        return sb.ToString();
    }
}

