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

    // ── Fixture-level setup guard ────────────────────────────────────────────
    // When OnFixtureSetUp throws, the exception is stored here instead of being
    // propagated to NUnit. This prevents tests from being silently omitted from
    // the Allure report. Instead, each test's TearDown rethrows the exception so
    // every test in the fixture appears in Allure with a meaningful failure.

    private Exception? _fixtureSetUpException;

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
    public void FixtureSetUp()
    {
        // Non-virtual, non-throwable entry point.
        // Calls the virtual OnFixtureSetUp() hook in a try-catch so that a failure
        // in fixture initialization is stored rather than propagated to NUnit.
        // NUnit will still start every test in the fixture; each one is then
        // failed in TearDown with the real root-cause exception.
        try
        {
            Logger = Log.ForContext(GetType());
            Logger.Information("Starting test fixture: {Fixture}", GetType().Name);
            OnFixtureSetUp();
        }
        catch (Exception ex)
        {
            // Ensure Logger is available for the warning even if ForContext threw.
            Logger ??= Log.ForContext(GetType());
            Logger.Warning(ex,
                "OneTimeSetUp failed for {Fixture} — tests will be reported as failed in TearDown",
                GetType().Name);
            _fixtureSetUpException = ex;
        }
    }

    /// <summary>
    /// Override this method to initialize fixture-scoped dependencies (instead of
    /// overriding <c>[OneTimeSetUp]</c> directly).
    ///
    /// Any exception thrown here is caught and re-raised per-test in <c>TearDown</c>,
    /// so every test in the fixture appears in the Allure report with a meaningful
    /// failure message instead of being silently omitted.
    /// </summary>
    protected virtual void OnFixtureSetUp() { }

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

        // Re-raise any fixture-level setup failure so each test appears in Allure
        // as failed with the real root cause rather than being silently omitted.
        if (_fixtureSetUpException is not null)
            throw new InvalidOperationException(
                $"[OneTimeSetUp] failed for {GetType().Name}: {_fixtureSetUpException.Message}",
                _fixtureSetUpException);
    }

    [OneTimeTearDown]
    public virtual void OneTimeTearDown()
    {
        Logger?.Information("Finished test fixture: {Fixture}", GetType().Name);
    }
}

