using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Waiting;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Framework.Tests;

[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Eventually")]
[Category("Unit")]
public class EventuallyTests : BaseTest
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(10);

    [Test]
    public async Task UntilAsync_ReturnsTheFirstValueThatSatisfiesTheCondition()
    {
        var calls = 0;

        var value = await Eventually.UntilAsync(() => Task.FromResult(++calls), n => n >= 3, "third call",
            TimeSpan.FromSeconds(2), Fast);

        value.Should().Be(3);
        calls.Should().Be(3);
    }

    [Test]
    public async Task UntilAsync_TreatsProbeExceptionsAsNotYet()
    {
        var calls = 0;

        var value = await Eventually.UntilAsync(async () =>
        {
            await Task.Yield();
            if (++calls < 3) throw new HttpRequestException("connection refused");
            return "ready";
        }, v => v == "ready", "service ready", TimeSpan.FromSeconds(2), Fast);

        value.Should().Be("ready");
    }

    [Test]
    public async Task UntilAsync_Timeout_FailsWithExpectationAttemptsAndLastValue()
    {
        var act = () => Eventually.UntilAsync(() => Task.FromResult(new { Status = "Pending" }),
            o => o.Status == "Shipped", "order is shipped", TimeSpan.FromMilliseconds(100), Fast);

        var failure = await act.Should().ThrowAsync<AssertionException>();
        failure.Which.Message.Should()
            .StartWith("Expected eventually: order is shipped — not met within 0.1 s")
            .And.Contain("attempt(s)")
            .And.Contain("""last value: {"Status":"Pending"}""");
    }

    [Test]
    public async Task UntilAsync_Timeout_ReportsTheLastException()
    {
        var act = () => Eventually.UntilAsync<int>(() => throw new InvalidOperationException("boom"),
            _ => true, "never", TimeSpan.FromMilliseconds(50), Fast);

        (await act.Should().ThrowAsync<AssertionException>())
            .Which.Message.Should().Contain("last attempt threw InvalidOperationException: boom");
    }

    [Test]
    public async Task SatisfiesAsync_RetriesFluentAssertionsUntilTheyPass()
    {
        var calls = 0;

        await Eventually.SatisfiesAsync(async () =>
        {
            await Task.Yield();
            (++calls).Should().BeGreaterThanOrEqualTo(4);
        }, "fourth attempt", TimeSpan.FromSeconds(2), Fast);

        calls.Should().Be(4);
    }
}
