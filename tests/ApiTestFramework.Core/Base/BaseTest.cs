using Allure.NUnit;
using ApiTestFramework.Core.TestData;
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

    [OneTimeSetUp]
    public virtual void OneTimeSetUp()
    {
        Logger = Log.ForContext(GetType());
        Logger.Information("Starting test fixture: {Fixture}", GetType().Name);
    }

    [SetUp]
    public virtual void SetUp()
    {
        Logger.Information("Starting test: {Test}", TestContext.CurrentContext.Test.Name);
    }

    [TearDown]
    public virtual async Task TearDown()
    {
        var outcome = TestContext.CurrentContext.Result.Outcome.Status;
        Logger.Information("Test {Test} finished with status: {Status}",
            TestContext.CurrentContext.Test.Name, outcome);

        // Delete resources this test created (orders before products — LIFO)
        await TestDataRegistry.CleanupCurrentTestAsync(Logger);
    }

    [OneTimeTearDown]
    public virtual void OneTimeTearDown()
    {
        Logger.Information("Finished test fixture: {Fixture}", GetType().Name);
    }
}
