using Allure.NUnit;
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
    protected ILogger Logger { get; private set; } = null!;

    // When fixture setup throws, NUnit fails every test of the fixture but
    // Allure.NUnit writes no result at all — the tests silently disappear from
    // the report. The exception is therefore stored here and re-raised per test
    // in SetUp, so each test shows up in Allure as failed with the root cause.
    private Exception? _fixtureSetUpException;

    private readonly List<Func<Task>> _cleanupActions = new();

    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        Logger = Log.ForContext(GetType());
        Logger.Information("Starting test fixture: {Fixture}", GetType().Name);

        try
        {
            OnFixtureSetUp();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex,
                "Fixture setup failed for {Fixture} — every test will be reported as failed",
                GetType().Name);
            _fixtureSetUpException = ex;
        }
    }

    /// <summary>
    /// Override to initialize fixture-scoped dependencies (steps, clients, ...)
    /// instead of declaring your own <c>[OneTimeSetUp]</c>. An exception thrown
    /// here fails every test of the fixture with the real root cause — and,
    /// unlike a failing <c>[OneTimeSetUp]</c>, keeps the tests in the Allure report.
    /// </summary>
    protected virtual void OnFixtureSetUp() { }

    /// <summary>
    /// Registers an async cleanup action that runs after the current test
    /// (in <c>[TearDown]</c>), whatever the test outcome. Actions run in LIFO
    /// order; a failing action is logged and does not stop the others.
    /// <code>
    /// var product = await _steps.CreateProductAsync();
    /// RegisterCleanup(() => _adminSteps.DeleteProductAsync(product.Id));
    /// </code>
    /// </summary>
    protected void RegisterCleanup(Func<Task> action) => _cleanupActions.Add(action);

    [SetUp]
    public virtual void SetUp()
    {
        _cleanupActions.Clear();
        Logger.Information("Starting test: {Test}", TestContext.CurrentContext.Test.Name);

        if (_fixtureSetUpException is not null)
            throw new InvalidOperationException(
                $"Fixture setup failed for {GetType().Name}: {_fixtureSetUpException.Message}",
                _fixtureSetUpException);
    }

    [TearDown]
    public virtual async Task TearDown()
    {
        for (var i = _cleanupActions.Count - 1; i >= 0; i--)
        {
            try
            {
                await _cleanupActions[i]();
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Cleanup action #{Index} failed (ignored)", i);
            }
        }
        _cleanupActions.Clear();

        var outcome = TestContext.CurrentContext.Result.Outcome.Status;
        Logger.Information("Test {Test} finished with status: {Status}",
            TestContext.CurrentContext.Test.Name, outcome);
    }

    [OneTimeTearDown]
    public virtual void OneTimeTearDown()
    {
        Logger.Information("Finished test fixture: {Fixture}", GetType().Name);
    }
}
