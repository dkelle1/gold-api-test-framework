using ApiTestFramework.Core.Auth;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Core.Client;

/// <summary>
/// Generic fluent request builder for constructing REST API requests.
/// Supports path, query parameters, headers, body, authentication, and more.
/// </summary>
public class RequestBuilder
{
    private string _resource = string.Empty;
    private Method _method = Method.Get;
    private readonly Dictionary<string, string> _headers = new();
    private readonly Dictionary<string, string> _queryParameters = new();
    private readonly Dictionary<string, string> _pathSegments = new();
    private object? _body;
    private string? _rawJsonBody;
    private string? _contentType;
    private int? _timeoutSeconds;

    private RequestBuilder() { }

    public static RequestBuilder Create() => new();

    /// <summary>
    /// Sets the HTTP method (GET, POST, PUT, DELETE, PATCH).
    /// </summary>
    public RequestBuilder WithMethod(Method method)
    {
        _method = method;
        return this;
    }

    /// <summary>
    /// Sets the resource path (e.g., "/api/products/{id}").
    /// </summary>
    public RequestBuilder WithPath(string path)
    {
        _resource = path;
        return this;
    }

    /// <summary>
    /// Adds a path segment parameter (replaces {key} in the path).
    /// </summary>
    public RequestBuilder WithPathSegment(string name, object value)
    {
        _pathSegments[name] = value.ToString()!;
        return this;
    }

    /// <summary>
    /// Adds a query parameter to the request.
    /// </summary>
    public RequestBuilder WithQueryParameter(string name, string value)
    {
        _queryParameters[name] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple query parameters.
    /// </summary>
    public RequestBuilder WithQueryParameters(Dictionary<string, string> parameters)
    {
        foreach (var param in parameters)
        {
            _queryParameters[param.Key] = param.Value;
        }
        return this;
    }

    /// <summary>
    /// Adds a custom header to the request.
    /// </summary>
    public RequestBuilder WithHeader(string name, string value)
    {
        _headers[name] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple headers.
    /// </summary>
    public RequestBuilder WithHeaders(Dictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            _headers[header.Key] = header.Value;
        }
        return this;
    }

    /// <summary>
    /// Adds a Bearer token authorization header.
    /// </summary>
    public RequestBuilder WithBearerToken(string token)
    {
        _headers["Authorization"] = $"Bearer {token}";
        return this;
    }

    /// <summary>
    /// Sets the request body (will be serialized as JSON by default).
    /// </summary>
    public RequestBuilder WithBody(object body)
    {
        _body = body;
        return this;
    }

    /// <summary>
    /// Sets a raw JSON string as the request body, bypassing serialization.
    /// Used by schema-driven negative tests to send deliberately malformed payloads.
    /// </summary>
    public RequestBuilder WithRawJsonBody(string json)
    {
        _rawJsonBody = json;
        return this;
    }

    /// <summary>
    /// Sets the content type for the request.
    /// </summary>
    public RequestBuilder WithContentType(string contentType)
    {
        _contentType = contentType;
        return this;
    }

    /// <summary>
    /// Sets a custom timeout for the request.
    /// </summary>
    public RequestBuilder WithTimeout(int seconds)
    {
        _timeoutSeconds = seconds;
        return this;
    }

    /// <summary>
    /// Builds the RestRequest from the configured parameters.
    /// Automatically injects the Bearer token from TokenProvider if available
    /// and no Authorization header has been set explicitly.
    /// </summary>
    public RestRequest Build()
    {
        // Auto-inject Bearer token if available and not already set
        if (!_headers.ContainsKey("Authorization") && TokenProvider.HasToken)
        {
            _headers["Authorization"] = $"Bearer {TokenProvider.Token}";
        }

        // Correlation header: lets service logs be matched to the test that
        // sent the request (services log it; Allure attachments include it)
        if (!_headers.ContainsKey("X-Test-Id"))
        {
            var testId = TestContext.CurrentContext?.Test?.FullName;
            if (!string.IsNullOrEmpty(testId))
            {
                // Header values must be printable ASCII; test names may not be
                var sanitized = new string(testId.Select(c => c is >= ' ' and <= '~' ? c : '_').ToArray());
                _headers["X-Test-Id"] = sanitized.Length > 200 ? sanitized[..200] : sanitized;
            }
        }

        var resource = _resource;

        // Replace path segments
        foreach (var segment in _pathSegments)
        {
            resource = resource.Replace($"{{{segment.Key}}}", segment.Value);
        }

        var request = new RestRequest(resource, _method);

        // Add query parameters
        foreach (var param in _queryParameters)
        {
            request.AddQueryParameter(param.Key, param.Value);
        }

        // Add headers
        foreach (var header in _headers)
        {
            request.AddHeader(header.Key, header.Value);
        }

        // Add body
        if (_rawJsonBody != null)
        {
            request.AddStringBody(_rawJsonBody, ContentType.Json);
        }
        else if (_body != null)
        {
            if (_contentType != null)
            {
                request.AddStringBody(
                    Newtonsoft.Json.JsonConvert.SerializeObject(_body),
                    ContentType.FromDataFormat(DataFormat.Json));
            }
            else
            {
                request.AddJsonBody(_body);
            }
        }

        // Set timeout
        if (_timeoutSeconds.HasValue)
        {
            request.Timeout = TimeSpan.FromSeconds(_timeoutSeconds.Value);
        }

        return request;
    }
}
