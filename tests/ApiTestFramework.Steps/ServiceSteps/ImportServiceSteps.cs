using System.Diagnostics;
using System.Net;
using System.Text;
using Allure.NUnit.Attributes;
using ApiTestFramework.Clients.ImportService;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Constants;
using ApiTestFramework.Core.DI;
using RestSharp;
using Serilog;

namespace ApiTestFramework.Steps.ServiceSteps;

public class ImportServiceSteps
{
    private readonly ApiClient _client;
    private readonly ILogger _logger;

    public ImportServiceSteps()
    {
        _client = ContainerProvider.ResolveNamed<ApiClient>("ImportService");
        _logger = Log.ForContext<ImportServiceSteps>();
    }

    [AllureStep("Start product CSV import")]
    public async Task<RestResponse<ImportAcceptedResponse>> StartCsvImportAsync(string csvFileName, string csvContent)
    {
        _logger.Information("Starting CSV import for file: {FileName}", csvFileName);

        var request = new RestRequest(ImportServiceRoutes.ProductCsvImport, Method.Post)
        {
            AlwaysMultipartFormData = true
        };

        var content = Encoding.UTF8.GetBytes(csvContent);
        request.AddFile("file", content, csvFileName, "text/csv");

        return await _client.ExecuteAsync<ImportAcceptedResponse>(request);
    }

    [AllureStep("Get import status for batch: {batchId}")]
    public async Task<RestResponse<ImportStatusResponse>> GetImportStatusAsync(Guid batchId)
    {
        return await _client.SendAsync<ImportStatusResponse>(
            RequestBuilder.Create()
                .WithMethod(Method.Get)
                .WithPath(ImportServiceRoutes.StatusByBatchId)
                .WithPathSegment("batchId", batchId));
    }

    [AllureStep("Wait for import completion for batch: {batchId}")]
    public async Task<ImportStatusResponse> WaitForImportCompletionAsync(Guid batchId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(90);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < effectiveTimeout)
        {
            var response = await GetImportStatusAsync(batchId);
            response.ShouldHaveStatusCode(HttpStatusCode.OK);

            var status = response.ShouldHaveData();
            if (string.Equals(status.Batch.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status.Batch.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Import batch {batchId} did not complete within {effectiveTimeout.TotalSeconds} seconds.");
    }
}
