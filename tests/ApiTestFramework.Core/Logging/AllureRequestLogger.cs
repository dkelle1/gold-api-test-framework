using System.Text;
using Allure.Net.Commons;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RestSharp;

namespace ApiTestFramework.Core.Logging;

/// <summary>
/// Attaches full request and response details to the current Allure step/test.
/// Automatically called from ApiClient after each request execution.
/// </summary>
public static class AllureRequestLogger
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
        Converters = { new StringEnumConverter() }
    };

    /// <summary>
    /// Logs full request details as an Allure attachment.
    /// </summary>
    public static void AttachRequest(RestRequest request, string baseUrl, string serviceName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== REQUEST [{serviceName}] ===");
        sb.AppendLine($"Method:   {request.Method}");
        sb.AppendLine($"URL:      {baseUrl}{request.Resource}");
        sb.AppendLine();

        // Headers
        var headers = request.Parameters
            .Where(p => p.Type == ParameterType.HttpHeader)
            .ToList();
        if (headers.Any())
        {
            sb.AppendLine("Headers:");
            foreach (var h in headers)
                sb.AppendLine($"  {h.Name}: {h.Value}");
            sb.AppendLine();
        }

        // Query parameters
        var queryParams = request.Parameters
            .Where(p => p.Type == ParameterType.QueryString)
            .ToList();
        if (queryParams.Any())
        {
            sb.AppendLine("Query Parameters:");
            foreach (var q in queryParams)
                sb.AppendLine($"  {q.Name}={q.Value}");
            sb.AppendLine();
        }

        // Body
        var bodyParam = request.Parameters
            .FirstOrDefault(p => p.Type == ParameterType.RequestBody);
        if (bodyParam != null)
        {
            sb.AppendLine("Body:");
            var bodyContent = bodyParam.Value?.ToString() ?? "(null)";
            sb.AppendLine(TryPrettyPrintJson(bodyContent));
        }

        TryAddAttachment(
            $"Request — {request.Method} {request.Resource}",
            "text/plain",
            Encoding.UTF8.GetBytes(sb.ToString()),
            ".txt");
    }

    /// <summary>
    /// Logs full response details as an Allure attachment.
    /// </summary>
    public static void AttachResponse(RestResponse response, string serviceName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== RESPONSE [{serviceName}] ===");
        sb.AppendLine($"Status:       {(int)response.StatusCode} {response.StatusCode}");
        sb.AppendLine($"Content-Type: {response.ContentType ?? "(none)"}");
        sb.AppendLine($"Duration:     (see Allure timeline)");
        sb.AppendLine($"URL:          {response.ResponseUri}");
        sb.AppendLine();

        // Response headers
        if (response.Headers != null && response.Headers.Any())
        {
            sb.AppendLine("Response Headers:");
            foreach (var h in response.Headers)
                sb.AppendLine($"  {h.Name}: {h.Value}");
            sb.AppendLine();
        }

        // Body
        sb.AppendLine("Body:");
        if (!string.IsNullOrWhiteSpace(response.Content))
        {
            sb.AppendLine(TryPrettyPrintJson(response.Content));
        }
        else
        {
            sb.AppendLine("  (empty)");
        }

        // Error info
        if (response.ErrorException != null)
        {
            sb.AppendLine();
            sb.AppendLine("Error Exception:");
            sb.AppendLine($"  {response.ErrorException.GetType().Name}: {response.ErrorException.Message}");
            if (response.ErrorException.InnerException != null)
                sb.AppendLine($"  Inner: {response.ErrorException.InnerException.Message}");
        }

        if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
        {
            sb.AppendLine($"Error Message: {response.ErrorMessage}");
        }

        TryAddAttachment(
            $"Response — {(int)response.StatusCode} {response.StatusCode}",
            "text/plain",
            Encoding.UTF8.GetBytes(sb.ToString()),
            ".txt");
    }

    /// <summary>
    /// Attaches a failure summary when a status code assertion fails.
    /// </summary>
    public static void AttachFailureSummary(RestResponse response, string reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ASSERTION FAILURE ===");
        sb.AppendLine($"Reason:       {reason}");
        sb.AppendLine($"Status Code:  {(int)response.StatusCode} {response.StatusCode}");
        sb.AppendLine($"URL:          {response.ResponseUri}");
        sb.AppendLine();
        sb.AppendLine("Response Body:");
        if (!string.IsNullOrWhiteSpace(response.Content))
            sb.AppendLine(TryPrettyPrintJson(response.Content));
        else
            sb.AppendLine("  (empty)");

        if (response.ErrorException != null)
        {
            sb.AppendLine();
            sb.AppendLine($"Exception:    {response.ErrorException.GetType().Name}: {response.ErrorException.Message}");
        }

        TryAddAttachment(
            "FAILURE DETAILS",
            "text/plain",
            Encoding.UTF8.GetBytes(sb.ToString()),
            ".txt");
    }

    /// <summary>
    /// Attaches a DTO comparison (expected vs actual) to Allure.
    /// </summary>
    public static void AttachDtoComparison<T>(T? expected, T? actual, string label = "DTO Comparison")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {label} ===");
        sb.AppendLine();
        sb.AppendLine("--- Expected ---");
        sb.AppendLine(SerializeObject(expected));
        sb.AppendLine();
        sb.AppendLine("--- Actual ---");
        sb.AppendLine(SerializeObject(actual));

        TryAddAttachment(
            label,
            "text/plain",
            Encoding.UTF8.GetBytes(sb.ToString()),
            ".txt");
    }

    /// <summary>
    /// Attaches a JSON body to Allure.
    /// </summary>
    public static void AttachJson(object? obj, string label)
    {
        var json = SerializeObject(obj);
        TryAddAttachment(
            label,
            "application/json",
            Encoding.UTF8.GetBytes(json),
            ".json");
    }

    private static string TryPrettyPrintJson(string input)
    {
        try
        {
            var obj = JsonConvert.DeserializeObject(input);
            return JsonConvert.SerializeObject(obj, JsonSettings);
        }
        catch
        {
            return input;
        }
    }

    private static string SerializeObject(object? obj)
    {
        if (obj == null) return "(null)";
        try
        {
            return JsonConvert.SerializeObject(obj, JsonSettings);
        }
        catch
        {
            return obj.ToString() ?? "(null)";
        }
    }

    /// <summary>
    /// Safely adds an Allure attachment, silently skipping if no Allure context is active
    /// (e.g., during GlobalSetup before any test has started).
    /// </summary>
    private static void TryAddAttachment(string name, string type, byte[] content, string extension)
    {
        try
        {
            AllureApi.AddAttachment(name, type, content, extension);
        }
        catch (InvalidOperationException)
        {
            // No Allure context (e.g., running in SetUpFixture) — skip silently
        }
    }
}
