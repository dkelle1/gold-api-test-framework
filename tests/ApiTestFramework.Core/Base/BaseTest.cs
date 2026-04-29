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
    /// <summary>An ID guaranteed to not exist in any service database.</summary>
    protected const int NonExistentId = 999_999;

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
    public virtual void TearDown()
    {
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
