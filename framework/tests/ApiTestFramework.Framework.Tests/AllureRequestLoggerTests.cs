using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Logging;
using FluentAssertions;
using NUnit.Framework;

namespace ApiTestFramework.Framework.Tests;

[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Allure Logging")]
[Category("Unit")]
public class AllureRequestLoggerTests : BaseTest
{
    private sealed record Item(int ProductId, string? Note);

    [Test]
    public void ObjectBody_IsShownAsCamelCaseJson_NotItsTypeName()
        => AllureRequestLogger.FormatBody(new Item(42, null)).Should().Be("""{"productId":42,"note":null}""");

    [Test]
    public void StringBody_IsShownAsIs()
        => AllureRequestLogger.FormatBody("Name,Price").Should().Be("Name,Price");

    [Test]
    public void MissingBody_IsShownAsNull()
        => AllureRequestLogger.FormatBody(null).Should().Be("(null)");
}
