using ApiTestFramework.Core.Logging;
using RestSharp;
using Serilog;

namespace ApiTestFramework.Core.Client;

/// <summary>
/// Generic API client wrapping RestSharp. Each service gets its own instance
/// configured with a base URL. Provides typed and untyped Execute methods.
/// Every request/response is automatically attached to the current Allure step.
/// Transient failures are retried up to <see cref="RetryCount"/> times with
/// exponential back-off (200 ms × 2^(attempt-1)) — see <see cref="ShouldRetry"/>.
/// </summary>
public class ApiClient : IDisposable
{
    private static readonly HashSet<Method> IdempotentMethods =
        new() { Method.Get, Method.Head, Method.Options, Method.Put, Method.Delete };

    private readonly RestClient _client;
    private readonly string _baseUrl;
    private readonly string _serviceName;
    private readonly ILogger _logger;

    /// <summary>
    /// Number of retry attempts on transient failures. Defaults to 0 (no retries).
    /// Set via <c>TestConfiguration.RetryCount</c> (appsettings.test.json: "RetryCount").
    /// </summary>
    public int RetryCount { get; init; }

    public ApiClient(string baseUrl, string serviceName)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _serviceName = serviceName;
        _logger = Log.ForContext<ApiClient>();

        var options = new RestClientOptions(baseUrl)
        {
            ThrowOnAnyError = false,
            Timeout = TimeSpan.FromSeconds(30)
        };

        _client = new RestClient(options);
    }

    /// <summary>
    /// Executes a request and returns the raw RestResponse.
    /// Full request and response are attached to Allure.
    /// </summary>
    public async Task<RestResponse> ExecuteAsync(RestRequest request)
    {
        _logger.Information("[{Service}] {Method} {Resource}", _serviceName, request.Method, request.Resource);

        AllureRequestLogger.AttachRequest(request, _baseUrl, _serviceName);

        var response = await ExecuteWithRetryAsync(request, () => _client.ExecuteAsync(request));

        _logger.Information("[{Service}] Response: {StatusCode} ({Length} bytes)",
            _serviceName, (int)response.StatusCode, response.Content?.Length ?? 0);

        AllureRequestLogger.AttachResponse(response, _serviceName);

        return response;
    }

    /// <summary>
    /// Executes a request and deserializes the response to the given type.
    /// Full request and response are attached to Allure.
    /// </summary>
    public async Task<RestResponse<T>> ExecuteAsync<T>(RestRequest request)
    {
        _logger.Information("[{Service}] {Method} {Resource}", _serviceName, request.Method, request.Resource);

        AllureRequestLogger.AttachRequest(request, _baseUrl, _serviceName);

        var response = await ExecuteWithRetryAsync(request, () => _client.ExecuteAsync<T>(request));

        _logger.Information("[{Service}] Response: {StatusCode} ({Length} bytes)",
            _serviceName, (int)response.StatusCode, response.Content?.Length ?? 0);

        AllureRequestLogger.AttachResponse(response, _serviceName);

        return response;
    }

    /// <summary>
    /// Convenience: send a request built with RequestBuilder.
    /// </summary>
    public Task<RestResponse> SendAsync(RequestBuilder builder)
        => ExecuteAsync(builder.Build());

    /// <summary>
    /// Convenience: send a request built with RequestBuilder with typed response.
    /// </summary>
    public Task<RestResponse<T>> SendAsync<T>(RequestBuilder builder)
        => ExecuteAsync<T>(builder.Build());

    private async Task<TResponse> ExecuteWithRetryAsync<TResponse>(
        RestRequest request, Func<Task<TResponse>> send) where TResponse : RestResponse
    {
        var response = await send();

        for (var attempt = 1; attempt <= RetryCount && ShouldRetry(request.Method, response); attempt++)
        {
            var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
            _logger.Warning("[{Service}] {Method} {Resource} failed ({Status}{Error}) — retry {Attempt}/{Max} in {Delay} ms",
                _serviceName, request.Method, request.Resource, (int)response.StatusCode,
                response.ErrorMessage is null ? "" : $", {response.ErrorMessage}",
                attempt, RetryCount, delay.TotalMilliseconds);

            await Task.Delay(delay);
            response = await send();
        }

        return response;
    }

    /// <summary>
    /// Decides whether a response is worth retrying.
    /// <list type="bullet">
    ///   <item>Idempotent methods (GET, HEAD, OPTIONS, PUT, DELETE): retried on
    ///   network errors, timeouts and 5xx responses.</item>
    ///   <item>Non-idempotent methods (POST, PATCH): retried only when no HTTP
    ///   response was received at all (e.g. connection refused) — a 5xx or a
    ///   timeout may mean the server already applied the change, and a retry
    ///   would create duplicates.</item>
    /// </list>
    /// 4xx responses are never retried — they are valid API behaviour under test.
    /// </summary>
    internal static bool ShouldRetry(Method method, RestResponse response)
    {
        var noResponse = response.StatusCode == 0 && response.ResponseStatus != ResponseStatus.TimedOut;
        if (!IdempotentMethods.Contains(method))
            return noResponse;

        return noResponse
            || response.ResponseStatus == ResponseStatus.TimedOut
            || (int)response.StatusCode >= 500;
    }

    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }
}
