using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using Serilog;

namespace ApiTestFramework.Core.Waiting;

/// <summary>
/// Polls until an eventually-consistent outcome is reached — the replacement for
/// <c>Thread.Sleep</c> in flows where a service applies a change asynchronously
/// (events, queues, background jobs). Fails with an <see cref="AssertionException"/>
/// that names the expectation, the number of attempts and the last observed value.
/// <code>
/// var order = await Eventually.UntilAsync(
///     () => _orders.GetOrderAsync(id),
///     r => r.Data?.Status == OrderStatus.Shipped,
///     "order is shipped");
///
/// await Eventually.SatisfiesAsync(async () =>
/// {
///     var history = await _orders.GetOrdersByCustomerEmailAsync(email);
///     history.Data.Should().ContainSingle(o => o.Id == id);   // FluentAssertions inside
/// }, "order appears in the customer's history");
/// </code>
/// </summary>
public static class Eventually
{
    /// <summary>Default time budget for a single wait.</summary>
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Default delay between attempts.</summary>
    public static TimeSpan DefaultPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Calls <paramref name="probe"/> until <paramref name="condition"/> holds for its result and
    /// returns that result. Exceptions thrown by the probe count as "not yet".
    /// </summary>
    public static async Task<T> UntilAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> condition,
        string expectation,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var budget = timeout ?? DefaultTimeout;
        var interval = pollInterval ?? DefaultPollInterval;
        var clock = Stopwatch.StartNew();
        var attempts = 0;
        T? last = default;
        var hasValue = false;
        Exception? lastError = null;

        while (true)
        {
            attempts++;
            try
            {
                last = await probe();
                hasValue = true;
                lastError = null;
                if (condition(last))
                {
                    Log.Debug("Eventually: '{Expectation}' met after {Attempts} attempt(s), {Elapsed} ms",
                        expectation, attempts, clock.ElapsedMilliseconds);
                    return last;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            if (clock.Elapsed + interval > budget)
                break;
            await Task.Delay(interval);
        }

        var observed = lastError is not null
            ? $"last attempt threw {lastError.GetType().Name}: {lastError.Message}"
            : hasValue ? $"last value: {Describe(last)}" : "no value observed";
        throw new AssertionException(
            $"Expected eventually: {expectation} — not met within {budget.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)} s " +
            $"({attempts} attempt(s)); {observed}");
    }

    /// <summary>
    /// Runs <paramref name="assertion"/> until it completes without throwing. Use FluentAssertions
    /// inside (its failures are plain exceptions); NUnit <c>Assert.*</c> failures are recorded on the
    /// test as soon as they happen and cannot be retried.
    /// </summary>
    public static async Task SatisfiesAsync(
        Func<Task> assertion,
        string expectation,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        await UntilAsync(async () => { await assertion(); return true; }, ok => ok, expectation, timeout, pollInterval);
    }

    private static string Describe<T>(T? value)
    {
        if (value is null)
            return "null";
        try
        {
            // RestResponse<T>: show status and body rather than the whole object graph
            var valueType = value.GetType();
            var status = valueType.GetProperty("StatusCode")?.GetValue(value);
            var content = valueType.GetProperty("Content")?.GetValue(value) as string;
            if (status is not null && content is not null)
                return $"HTTP {(int)status} {Truncate(content)}";

            return Truncate(JsonSerializer.Serialize(value));
        }
        catch (Exception)
        {
            return Truncate(value.ToString() ?? value.GetType().Name);
        }
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..297] + "...";
}
