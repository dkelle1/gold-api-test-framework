using ApiTestFramework.Core.Auth;
using ApiTestFramework.Core.Logging;
using RestSharp;
using Serilog;

namespace ApiTestFramework.Core.Client;

/// <summary>
/// Generic API client wrapping RestSharp. Each service gets its own instance
/// configured with a base URL. Provides typed and untyped Execute methods.
/// Every request/response is automatically attached to the current Allure step.
/// </summary>
public class ApiClient : IDisposable
{
    private readonly RestClient _client;
    private readonly string _baseUrl;
    private readonly string _serviceName;
    private readonly ILogger _logger;

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

        var response = await _client.ExecuteAsync(request);

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

        var response = await _client.ExecuteAsync<T>(request);

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
    /// Auto-injects a Bearer token from <see cref="TokenProvider"/> unless the request
    /// already carries an Authorization header (explicit override, e.g. in negative auth tests).
    /// </summary>
    private void InjectBearerToken(RestRequest request)
    {
        if (TokenProvider.HasToken &&
            !request.Parameters.Any(p =>
                p.Type == ParameterType.HttpHeader &&
                string.Equals(p.Name, "Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            request.AddHeader("Authorization", $"Bearer {TokenProvider.Token}");
        }
    }
}
