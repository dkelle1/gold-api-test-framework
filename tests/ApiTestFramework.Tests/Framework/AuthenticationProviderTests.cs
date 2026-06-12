using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Auth;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Tests.Framework;

/// <summary>
/// Unit tests for the pluggable authentication layer — no live services needed.
/// Documents how adopters with non-JWT authorization plug into the framework.
/// </summary>
[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Pluggable Authentication")]
[Category("Unit")]
public class AuthenticationProviderTests : BaseTest
{
    private IAuthenticationProvider _originalProvider = null!;

    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _originalProvider = AuthenticationContext.Provider;
    }

    public override void OneTimeTearDown()
    {
        AuthenticationContext.Provider = _originalProvider;
        base.OneTimeTearDown();
    }

    [Test]
    public void StaticTokenProvider_ReturnsBearerHeader()
    {
        var provider = new StaticTokenAuthenticationProvider("my-token");

        var header = provider.GetAuthenticationHeader();

        header.Should().Be(new AuthenticationHeader("Authorization", "Bearer my-token"));
    }

    [Test]
    public void StaticTokenProvider_WithCustomScheme_UsesScheme()
    {
        var provider = new StaticTokenAuthenticationProvider("abc", scheme: "Token");

        provider.GetAuthenticationHeader()!.Value.Should().Be("Token abc");
    }

    [Test]
    public void StaticTokenProvider_WithEmptyScheme_SendsRawToken()
    {
        var provider = new StaticTokenAuthenticationProvider("raw-value", scheme: "");

        provider.GetAuthenticationHeader()!.Value.Should().Be("raw-value");
    }

    [Test]
    public void ApiKeyProvider_UsesConfiguredHeader()
    {
        var provider = new ApiKeyAuthenticationProvider("secret-key", "X-Api-Key");

        provider.GetAuthenticationHeader()
            .Should().Be(new AuthenticationHeader("X-Api-Key", "secret-key"));
    }

    [Test]
    public void ApiKeyProvider_WithoutKey_ReturnsNoHeader()
    {
        new ApiKeyAuthenticationProvider("").GetAuthenticationHeader().Should().BeNull();
    }

    [Test]
    public void NoAuthProvider_ReturnsNoHeader()
    {
        new NoAuthenticationProvider().GetAuthenticationHeader().Should().BeNull();
    }

    [Test]
    public void DelegateProvider_ReflectsCurrentTokenValue()
    {
        string? currentToken = null;
        var provider = new DelegateAuthenticationProvider(() => currentToken);

        provider.GetAuthenticationHeader().Should().BeNull();

        currentToken = "fresh";
        provider.GetAuthenticationHeader()!.Value.Should().Be("Bearer fresh");
    }

    [Test]
    public void Factory_CreatesProviderMatchingConfiguredMode()
    {
        AuthenticationProviderFactory
            .Create(new AuthenticationOptions { Mode = "SessionToken" })
            .Should().BeOfType<SessionTokenAuthenticationProvider>();

        AuthenticationProviderFactory
            .Create(new AuthenticationOptions { Mode = "ApiKey", ApiKey = "k" })
            .Should().BeOfType<ApiKeyAuthenticationProvider>();

        AuthenticationProviderFactory
            .Create(new AuthenticationOptions { Mode = "None" })
            .Should().BeOfType<NoAuthenticationProvider>();

        AuthenticationProviderFactory
            .Create(new AuthenticationOptions { Mode = "StaticToken", Token = "t" })
            .Should().BeOfType<StaticTokenAuthenticationProvider>();
    }

    [Test]
    public void Factory_UnknownMode_ThrowsDescriptiveError()
    {
        var act = () => AuthenticationProviderFactory
            .Create(new AuthenticationOptions { Mode = "Kerberos" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Kerberos*Supported*");
    }

    [Test]
    public void RequestBuilder_InjectsHeaderFromActiveProvider()
    {
        AuthenticationContext.Provider = new ApiKeyAuthenticationProvider("the-key", "X-Api-Key");

        var request = RequestBuilder.Create()
            .WithMethod(Method.Get)
            .WithPath("/api/anything")
            .Build();

        GetHeader(request, "X-Api-Key").Should().Be("the-key");
        GetHeader(request, "Authorization").Should().BeNull();
    }

    [Test]
    public void RequestBuilder_ExplicitHeader_SuppressesAutoInjection()
    {
        AuthenticationContext.Provider = new StaticTokenAuthenticationProvider("auto-token");

        // the documented 401-scenario pattern: blank out Authorization explicitly
        var request = RequestBuilder.Create()
            .WithMethod(Method.Get)
            .WithPath("/api/anything")
            .WithHeader("Authorization", "")
            .Build();

        GetHeader(request, "Authorization").Should().BeEmpty();
    }

    [Test]
    public void RequestBuilder_ExplicitBearerToken_WinsOverProvider()
    {
        AuthenticationContext.Provider = new StaticTokenAuthenticationProvider("auto-token");

        var request = RequestBuilder.Create()
            .WithMethod(Method.Get)
            .WithPath("/api/anything")
            .WithBearerToken("explicit-token")
            .Build();

        GetHeader(request, "Authorization").Should().Be("Bearer explicit-token");
    }

    private static string? GetHeader(RestRequest request, string name)
        => request.Parameters
            .FirstOrDefault(p => p.Type == ParameterType.HttpHeader && p.Name == name)
            ?.Value?.ToString();
}
