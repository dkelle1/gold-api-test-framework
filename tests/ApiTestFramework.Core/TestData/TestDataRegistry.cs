using System.Collections.Concurrent;
using NUnit.Framework;
using Serilog;

namespace ApiTestFramework.Core.TestData;

/// <summary>
/// Tracks resources created during a test (products, orders, users…) and
/// deletes them when the test finishes, so tests do not leak data into the
/// shared database and stay independent of each other.
/// <para>
/// Step classes call <see cref="Register"/> right after creating a resource;
/// <c>BaseTest.TearDown</c> calls <see cref="CleanupCurrentTestAsync"/>.
/// Resources are cleaned up LIFO (an order created after its product is
/// deleted before that product). Cleanup failures (e.g. 404 because the test
/// already deleted the resource itself) are logged and swallowed — cleanup
/// must never fail a passing test.
/// </para>
/// <para>
/// Keyed by the NUnit test id, so it is safe under parallel test execution.
/// </para>
/// </summary>
public static class TestDataRegistry
{
    private sealed record RegisteredResource(string Description, Func<Task> Cleanup);

    private static readonly ConcurrentDictionary<string, ConcurrentStack<RegisteredResource>> Resources = new();

    private static string CurrentTestKey =>
        TestContext.CurrentContext?.Test?.ID ?? "__no-test-context__";

    /// <summary>
    /// Registers a cleanup action for a resource created by the current test.
    /// </summary>
    /// <param name="description">Human-readable label, e.g. "Order 42".</param>
    /// <param name="cleanup">Delete action; a 404 inside is expected and fine.</param>
    public static void Register(string description, Func<Task> cleanup)
    {
        var stack = Resources.GetOrAdd(CurrentTestKey, _ => new ConcurrentStack<RegisteredResource>());
        stack.Push(new RegisteredResource(description, cleanup));
    }

    /// <summary>
    /// Runs all cleanup actions registered by the current test (LIFO) and
    /// forgets them. Never throws.
    /// </summary>
    public static async Task CleanupCurrentTestAsync(ILogger? logger = null)
    {
        if (!Resources.TryRemove(CurrentTestKey, out var stack) || stack.IsEmpty)
            return;

        var cleaned = 0;
        while (stack.TryPop(out var resource))
        {
            try
            {
                await resource.Cleanup();
                cleaned++;
            }
            catch (Exception ex)
            {
                logger?.Debug("Cleanup of {Resource} failed (ignored): {Error}",
                    resource.Description, ex.Message);
            }
        }

        logger?.Information("Cleaned up {Count} test resource(s)", cleaned);
    }

    /// <summary>
    /// Number of pending cleanups for the current test (diagnostics/tests).
    /// </summary>
    public static int PendingCount =>
        Resources.TryGetValue(CurrentTestKey, out var stack) ? stack.Count : 0;
}
