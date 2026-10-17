using System.Net;
using ApiTestFramework.Core.Logging;
using FluentAssertions;
using FluentAssertions.Equivalency;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RestSharp;

namespace ApiTestFramework.Core.Assertions;

/// <summary>
/// Extension methods for clean response assertions using FluentAssertions.
/// On failure, full details are attached to the Allure report.
/// </summary>
public static class ResponseAssertions
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
        Converters = { new StringEnumConverter() }
    };

    // ──────────────────────────────────────────────
    //  Status code assertions
    // ──────────────────────────────────────────────

    public static RestResponse ShouldHaveStatusCode(this RestResponse response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            AllureRequestLogger.AttachFailureSummary(response,
                $"Expected status {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}");
        }

        response.StatusCode.Should().Be(expected,
            $"Expected status {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}.\n" +
            $"URL: {response.ResponseUri}\n" +
            $"Response body: {response.Content}");
        return response;
    }

    public static RestResponse ShouldBeSuccessful(this RestResponse response)
    {
        if ((int)response.StatusCode < 200 || (int)response.StatusCode > 299)
        {
            AllureRequestLogger.AttachFailureSummary(response,
                $"Expected success (2xx) but got {(int)response.StatusCode} {response.StatusCode}");
        }

        ((int)response.StatusCode).Should().BeInRange(200, 299,
            $"Expected success status but got {(int)response.StatusCode} {response.StatusCode}.\n" +
            $"URL: {response.ResponseUri}\n" +
            $"Response body: {response.Content}");
        return response;
    }

    public static RestResponse<T> ShouldHaveStatusCode<T>(this RestResponse<T> response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            AllureRequestLogger.AttachFailureSummary(response,
                $"Expected status {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}");
        }

        response.StatusCode.Should().Be(expected,
            $"Expected status {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}.\n" +
            $"URL: {response.ResponseUri}\n" +
            $"Response body: {response.Content}");
        return response;
    }

    public static RestResponse<T> ShouldBeSuccessful<T>(this RestResponse<T> response)
    {
        if ((int)response.StatusCode < 200 || (int)response.StatusCode > 299)
        {
            AllureRequestLogger.AttachFailureSummary(response,
                $"Expected success (2xx) but got {(int)response.StatusCode} {response.StatusCode}");
        }

        ((int)response.StatusCode).Should().BeInRange(200, 299,
            $"Expected success status but got {(int)response.StatusCode} {response.StatusCode}.\n" +
            $"URL: {response.ResponseUri}\n" +
            $"Response body: {response.Content}");
        return response;
    }

    // ──────────────────────────────────────────────
    //  Data extraction
    // ──────────────────────────────────────────────

    public static T ShouldHaveData<T>(this RestResponse<T> response)
    {
        if (response.Data == null)
        {
            AllureRequestLogger.AttachFailureSummary(response,
                $"Response data is null. Status: {(int)response.StatusCode}. Body: {response.Content}");
        }

        response.Data.Should().NotBeNull(
            $"Response data should not be null.\n" +
            $"Status: {(int)response.StatusCode}\n" +
            $"Body: {response.Content}");
        return response.Data!;
    }

    public static RestResponse ShouldHaveContent(this RestResponse response)
    {
        if (string.IsNullOrEmpty(response.Content))
        {
            AllureRequestLogger.AttachFailureSummary(response,
                $"Response content is null or empty. Status: {(int)response.StatusCode}");
        }

        response.Content.Should().NotBeNullOrEmpty(
            $"Response content should not be null or empty.\n" +
            $"Status: {(int)response.StatusCode}");
        return response;
    }

    // ──────────────────────────────────────────────
    //  Full DTO validation (expected vs actual)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Validates that the deserialized response DTO is equivalent to the expected object.
    /// Attaches both expected and actual as Allure attachments for easy comparison.
    /// Uses FluentAssertions BeEquivalentTo for deep structural comparison.
    /// </summary>
    /// <param name="response">The REST response with deserialized data.</param>
    /// <param name="expected">The expected DTO to compare against.</param>
    /// <param name="label">Label for the Allure attachment.</param>
    /// <param name="config">Optional FluentAssertions equivalency config (e.g., to exclude properties).</param>
    public static RestResponse<T> ShouldMatchDto<T>(
        this RestResponse<T> response,
        T expected,
        string label = "DTO Validation",
        Func<EquivalencyAssertionOptions<T>, EquivalencyAssertionOptions<T>>? config = null)
    {
        var actual = response.Data;

        // Always attach comparison to Allure
        AllureRequestLogger.AttachDtoComparison(expected, actual, label);

        if (actual == null)
        {
            AllureRequestLogger.AttachFailureSummary(response,
                "Cannot validate DTO: actual data is null");
            actual.Should().NotBeNull($"Response data should not be null for DTO comparison.\n" +
                $"Status: {(int)response.StatusCode}\nBody: {response.Content}");
        }

        if (config != null)
            actual.Should().BeEquivalentTo(expected, config);
        else
            actual.Should().BeEquivalentTo(expected);

        return response;
    }

    /// <summary>
    /// Validates that the deserialized response DTO is equivalent to the expected object,
    /// excluding specified properties (e.g., server-generated Id, timestamps).
    /// </summary>
    public static RestResponse<T> ShouldMatchDtoExcluding<T>(
        this RestResponse<T> response,
        T expected,
        params string[] excludeProperties)
    {
        return response.ShouldMatchDto(expected, "DTO Validation (with exclusions)", options =>
        {
            foreach (var prop in excludeProperties)
                options = options.Excluding(ctx => ctx.Path == prop);
            return options;
        });
    }
}
