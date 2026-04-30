using Allure.NUnit;
using ApiTestFramework.Core.Auth;
using NUnit.Framework;
using Serilog;

namespace ApiTestFramework.Core.Base;

/// <summary>
/// Base test class that all test fixtures should inherit from.
/// Provides Allure reporting integration, logging, and common test lifecycle hooks.
/// </summary>
[AllureNUnit]
public abstract class BaseTest
{
    /// <summary>An ID guaranteed to not exist in any service database.</summary>
    protected const int NonExistentId = 999_999;

    protected ILogger Logger { get; private set; } = null!;

    // ── Per-test token override ──────────────────────────────────────────────

    /// <summary>
    /// Overrides the Bearer token for the current test's async execution context.
    /// The previous token (or no token) is restored when the returned scope is disposed.
    ///
    /// Use this when a single test needs to send one or more requests as a specific user
    /// without changing the global token.
    ///
    /// <code>
    /// using var scope = UseToken(otherUserToken);
    /// var response = await _productClient.SendAsync(RequestFactory.Get("/api/products"));
    /// // Previous token restored here
    /// </code>
    ///
    /// For creating a fresh user <i>and</i> entering the scope in one step, prefer
    /// <see cref="ApiTestFramework.Steps.ServiceSteps.AuthServiceSteps.CreateUserScopeAsync"/>.
    /// </summary>
    protected static TokenScope UseToken(string token) => TokenScope.Use(token);

    // ── Test data cleanup registry ───────────────────────────────────────────────

    private readonly List<Func<Task>> _cleanupActions = new();

    /// <summary>
    /// Registers an async cleanup action that will be executed at the end of the current
    /// test (<c>[TearDown]</c>). Use this to delete entities created during a test so that
    /// each test is isolated and the database doesn't accumulate stale data across runs.
    ///
    /// <code>
    /// var product = await _steps.CreateProductAsync();
    /// RegisterCleanup(() => _steps.DeleteProductAsync(product.Id));
    /// </code>
    ///
    /// Cleanup actions run in LIFO order (last registered → first executed) and individual
    /// failures are swallowed so that all registered actions always run.
    /// </summary>
    protected void RegisterCleanup(Func<Task> action) => _cleanupActions.Add(action);

    [OneTimeSetUp]
    public virtual void OneTimeSetUp()
    {
        Logger = Log.ForContext(GetType());
        Logger.Information("Starting test fixture: {Fixture}", GetType().Name);
    }

    [SetUp]
    public virtual void SetUp()
    {
        _cleanupActions.Clear();
        Logger.Information("Starting test: {Test}", TestContext.CurrentContext.Test.Name);
    }

    [TearDown]
    public virtual async Task TearDown()
    {
        var outcome = TestContext.CurrentContext.Result.Outcome.Status;
        Logger.Information("Test {Test} finished with status: {Status}",
            TestContext.CurrentContext.Test.Name, outcome);

        // Run cleanup actions in LIFO order; swallow individual failures.
        for (int i = _cleanupActions.Count - 1; i >= 0; i--)
        {
            try { await _cleanupActions[i](); }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Cleanup action #{Index} failed (swallowed)", i);
            }
        }

        _cleanupActions.Clear();
    }

    [OneTimeTearDown]
    public virtual void OneTimeTearDown()
    {
        Logger.Information("Finished test fixture: {Fixture}", GetType().Name);
    }
}

