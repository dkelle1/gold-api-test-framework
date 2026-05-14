using ApiTestFramework.Core.Auth;
using ApiTestFramework.Core.Logging;
using RestSharp;
using Serilog;

namespace ApiTestFramework.Core.Client;

/// <summary>
/// Generic API client wrapping RestSharp. Each service gets its own instance
/// configured with a base URL. Provides typed and untyped Execute methods.
/// Every request/response is automatically attached to the current Allure step.
/// Transient failures are retried up to <see cref="RetryCount"/> times with
/// exponential back-off (200 ms × 2^attempt).
/// </summary>
public class ApiClient : IDisposable
{
    private readonly RestClient _client;
    private readonly string _baseUrl;
    private readonly string _serviceName;
    private readonly ILogger _logger;

    /// <summary>
    /// Number of retry attempts on transient HTTP errors (5xx, timeout).
    /// Defaults to 0 (no retries). Set via <c>TestConfiguration.RetryCount</c>.
    /// </summary>
    public int RetryCount { get; set; } = 0;

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
        InjectBearerToken(request);
        _logger.Information("[{Service}] {Method} {Resource}", _serviceName, request.Method, request.Resource);

        AllureRequestLogger.AttachRequest(request, _baseUrl, _serviceName);

        RestResponse response = null!;
        for (int attempt = 0; attempt <= RetryCount; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                _logger.Warning("[{Service}] Retry {Attempt}/{Max} after {Delay}ms (status: {Status})",
                    _serviceName, attempt, RetryCount, delay.TotalMilliseconds, (int)response.StatusCode);
                await Task.Delay(delay);
            }

            response = await _client.ExecuteAsync(request);

            if (!IsTransient(response)) break;
        }

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
        InjectBearerToken(request);
        _logger.Information("[{Service}] {Method} {Resource}", _serviceName, request.Method, request.Resource);

        AllureRequestLogger.AttachRequest(request, _baseUrl, _serviceName);

        RestResponse<T> response = null!;
        for (int attempt = 0; attempt <= RetryCount; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                _logger.Warning("[{Service}] Retry {Attempt}/{Max} after {Delay}ms (status: {Status})",
                    _serviceName, attempt, RetryCount, delay.TotalMilliseconds, (int)response.StatusCode);
                await Task.Delay(delay);
            }

            response = await _client.ExecuteAsync<T>(request);

            if (!IsTransient(response)) break;
        }

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

    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Returns true for responses that are worth retrying (server errors or network failures).
    /// 4xx responses are not retried — they represent valid API behaviour.
    /// </summary>
    private static bool IsTransient(RestResponseBase response)
        => response.ResponseStatus == ResponseStatus.TimedOut
           || response.ResponseStatus == ResponseStatus.Error
           || (int)response.StatusCode >= 500;

    /// <summary>
    /// Auto-injects a Bearer token unless the request already carries an explicit
    /// Authorization header (negative auth tests set an empty string to suppress injection).
    /// Priority: per-test <see cref="TestTokenContext"/> → global <see cref="TokenProvider"/>.
    /// </summary>
    private void InjectBearerToken(RestRequest request)
    {
        // Skip injection if the caller explicitly set an Authorization header.
        bool hasExplicitHeader = request.Parameters.Any(p =>
            p.Type == ParameterType.HttpHeader &&
            string.Equals(p.Name, "Authorization", StringComparison.OrdinalIgnoreCase));

        if (hasExplicitHeader) return;

        // 1. Per-test token (AsyncLocal — parallel-safe, per-user)
        if (TestTokenContext.HasToken)
        {
            request.AddHeader("Authorization", $"Bearer {TestTokenContext.Token}");
            return;
        }

        // 2. Global fallback token (backwards-compatible with GlobalSetup single-token approach)
        if (TokenProvider.HasToken)
        {
            request.AddHeader("Authorization", $"Bearer {TokenProvider.Token}");
        }
    }
}
