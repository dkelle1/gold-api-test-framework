using System.Text.Json;
using ApiTestFramework.Core.Auth;
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
    private string? _contentType;
    private int? _timeoutSeconds;
    private string? _identity;
    private bool _suppressAuth;
    private bool _skipResponseValidation;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

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
    /// An explicit Authorization header overrides AsIdentity/WithoutAuth.
    /// </summary>
    public RequestBuilder WithHeader(string name, string value)
    {
        _headers[name] = value;
        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            _identity = null;
            _suppressAuth = false;
        }
        return this;
    }

    /// <summary>
    /// Adds multiple headers.
    /// </summary>
    public RequestBuilder WithHeaders(Dictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            WithHeader(header.Key, header.Value);
        }
        return this;
    }

    /// <summary>
    /// Adds a Bearer token authorization header.
    /// </summary>
    public RequestBuilder WithBearerToken(string token)
    {
        _headers["Authorization"] = $"Bearer {token}";
        _identity = null;
        _suppressAuth = false;
        return this;
    }

    /// <summary>
    /// Sends the request as the given named identity (e.g. "admin").
    /// The token is resolved from TokenProvider at build time and overrides
    /// the default auto-injected token. The last auth-related call wins.
    /// </summary>
    public RequestBuilder AsIdentity(string identity)
    {
        _identity = identity;
        _headers.Remove("Authorization");
        _suppressAuth = false;
        return this;
    }

    /// <summary>
    /// Sends the request without any Authorization header, disabling automatic
    /// token injection. Useful for unauthorized (401) test scenarios.
    /// </summary>
    public RequestBuilder WithoutAuth()
    {
        _suppressAuth = true;
        _identity = null;
        _headers.Remove("Authorization");
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
    /// Sends the body with the given content type (e.g. "application/merge-patch+json").
    /// A string body is sent as-is; any other object is serialized as JSON (camelCase).
    /// Without this call the body is sent as application/json.
    /// </summary>
    public RequestBuilder WithContentType(string contentType)
    {
        _contentType = contentType;
        return this;
    }

    /// <summary>
    /// Skips the registered response validators (e.g. contract validation) for this request —
    /// for requests that deliberately provoke undocumented behaviour.
    /// </summary>
    public RequestBuilder WithoutResponseValidation()
    {
        _skipResponseValidation = true;
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
    /// Auth resolution order: WithoutAuth() sends no authentication header at all;
    /// AsIdentity() uses the named token from TokenProvider; an explicitly set
    /// header is kept as-is; otherwise the header produced by the active
    /// <see cref="AuthenticationContext.Provider"/> (Bearer token, API key,
    /// OAuth2, ... depending on configuration) is auto-injected.
    /// </summary>
    public RestRequest Build()
    {
        var authHeader = AuthenticationContext.Provider.GetAuthenticationHeader();
        if (_suppressAuth)
        {
            _headers.Remove("Authorization");
            if (authHeader is not null)
                _headers.Remove(authHeader.Name);
        }
        else if (_identity != null)
        {
            _headers["Authorization"] = $"Bearer {TokenProvider.GetRequiredToken(_identity)}";
        }
        else if (authHeader is not null && !_headers.ContainsKey(authHeader.Name))
        {
            _headers[authHeader.Name] = authHeader.Value;
        }

        var resource = _resource;

        // Replace path segments
        foreach (var segment in _pathSegments)
        {
            resource = resource.Replace($"{{{segment.Key}}}", segment.Value);
        }

        var request = new TemplatedRestRequest(resource, _method, _resource)
        {
            SkipResponseValidation = _skipResponseValidation
        };

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
        if (_body != null)
        {
            if (_contentType != null)
            {
                var text = _body as string ?? JsonSerializer.Serialize(_body, WebJson);
                request.AddStringBody(text, _contentType);
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
