using ApiTestFramework.Core.Client;
using RestSharp;

namespace ApiTestFramework.Contracts;

/// <summary>
/// Explicit contract assertions. Require <see cref="ContractValidation.Enable"/> (any mode,
/// including Off) so the contracts are loaded.
/// </summary>
public static class ContractAssertions
{
    /// <summary>
    /// Asserts that the response conforms to the OpenAPI contract: the operation and the status
    /// code are documented and the body matches the documented schema.
    /// </summary>
    /// <param name="response">A response returned by ApiClient.</param>
    /// <param name="service">Contract name; omit to find the contract by request path.</param>
    public static RestResponse ShouldMatchContract(this RestResponse response, string? service = null)
    {
        Check(response, service);
        return response;
    }

    /// <inheritdoc cref="ShouldMatchContract(RestResponse, string?)"/>
    public static RestResponse<T> ShouldMatchContract<T>(this RestResponse<T> response, string? service = null)
    {
        Check(response, service);
        return response;
    }

    private static void Check(RestResponse response, string? service)
    {
        var validator = ContractValidation.Validator
            ?? throw new InvalidOperationException(
                "Contract validation is not enabled. Call ContractValidation.EnableFromConfiguration() in the global setup.");
        var request = response.Request
            ?? throw new InvalidOperationException("The response carries no request; use a response returned by ApiClient.");

        var check = validator.Check(service, request.Method.ToString(), request.Resource,
            (request as TemplatedRestRequest)?.PathTemplate, (int)response.StatusCode,
            response.ContentType, response.Content);

        ContractValidation.Report(check, response, fail: true);
    }
}
