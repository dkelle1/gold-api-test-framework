using RestSharp;

namespace ApiTestFramework.Core.Client;

/// <summary>
/// A check applied by <see cref="ApiClient"/> to every response — e.g. OpenAPI contract
/// validation (ApiTestFramework.Contracts). Throwing from <see cref="Validate"/> fails the
/// calling test at the request that produced the bad response.
/// </summary>
public interface IResponseValidator
{
    void Validate(ResponseValidationContext context);
}

/// <summary>What a validator gets to see for one request/response pair.</summary>
/// <param name="ServiceName">Name of the <see cref="ApiClient"/> (the Services key unless a Name is configured).</param>
/// <param name="Request">The request as sent.</param>
/// <param name="PathTemplate">Route template the request was built from (e.g. "/api/orders/{id}"), when known.</param>
/// <param name="Response">The final response (after retries).</param>
public sealed record ResponseValidationContext(
    string ServiceName,
    RestRequest Request,
    string? PathTemplate,
    RestResponse Response);

/// <summary>
/// Process-wide registry of response validators, consulted by every <see cref="ApiClient"/>.
/// Register once in the global setup; requests built with
/// <see cref="RequestBuilder.WithoutResponseValidation"/> are skipped.
/// </summary>
public static class ResponseValidators
{
    private static readonly List<IResponseValidator> Registered = new();
    private static readonly object Lock = new();

    public static void Register(IResponseValidator validator)
    {
        lock (Lock) Registered.Add(validator);
    }

    public static void Clear()
    {
        lock (Lock) Registered.Clear();
    }

    public static IReadOnlyList<IResponseValidator> All
    {
        get { lock (Lock) return Registered.ToArray(); }
    }

    internal static void Run(ResponseValidationContext context)
    {
        if (context.Request is TemplatedRestRequest { SkipResponseValidation: true })
            return;

        foreach (var validator in All)
            validator.Validate(context);
    }
}

/// <summary>
/// RestRequest produced by <see cref="RequestBuilder"/>: remembers the route template and
/// whether response validators should run.
/// </summary>
public sealed class TemplatedRestRequest : RestRequest
{
    public TemplatedRestRequest(string resource, Method method, string pathTemplate)
        : base(resource, method)
    {
        PathTemplate = pathTemplate;
    }

    /// <summary>The route as passed to <see cref="RequestBuilder.WithPath"/>, before path segments were filled in.</summary>
    public string PathTemplate { get; }

    public bool SkipResponseValidation { get; init; }
}
