using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Base;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Framework.Tests;

/// <summary>
/// Verifies BaseTest.RegisterCleanup: actions run after the test in LIFO order,
/// and a failing action neither fails the test nor stops the remaining actions.
/// The second test observes what the first test's TearDown did.
/// </summary>
[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Test Lifecycle")]
[Category("Unit")]
public class BaseTestCleanupTests : BaseTest
{
    private readonly List<string> _executed = new();

    [Test, Order(1)]
    public void RegisterCleanup_ActionsAreDeferredUntilTearDown()
    {
        RegisterCleanup(() => { _executed.Add("first"); return Task.CompletedTask; });
        RegisterCleanup(() => throw new InvalidOperationException("cleanup failure is ignored"));
        RegisterCleanup(async () => { await Task.Yield(); _executed.Add("third"); });

        _executed.Should().BeEmpty();
    }

    [Test, Order(2)]
    public void RegisterCleanup_RanInReverseOrder_DespiteFailingAction()
    {
        _executed.Should().Equal("third", "first");
    }
}
