using System.Net;
using System.Text;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Assertions;
using ApiTestFramework.Core.Auth;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Configuration;
using DemoShop.Clients.AuthService;
using DemoShop.Steps.ServiceSteps;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace DemoShop.Tests.AuthService;

/// <summary>
/// The shared authorization server's token endpoint (R-CLIENT-1, R-CLIENT-2, R-CLIENT-5).
/// Client credentials come from the same configuration the framework uses
/// (Authentication:Clients), so CI secrets override them in one place.
/// </summary>
[TestFixture]
[AllureSuite("AuthService")]
[AllureFeature("OAuth2 Client Credentials")]
public class TokenEndpointTests : BaseTest
{
    private AuthServiceSteps _auth = null!;
    private OAuth2Options _catalog = null!;

    protected override void OnFixtureSetUp()
    {
        _auth = new AuthServiceSteps();
        _catalog = AuthenticationProviderFactory
            .BindClientOptions(ConfigurationProvider.Configuration, AuthClients.Catalog).OAuth2;
    }

    private Dictionary<string, string> CatalogForm(string? scope = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _catalog.ClientId,
            ["client_secret"] = _catalog.ClientSecret,
        };
        if (scope is not null)
            form["scope"] = scope;
        return form;
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("R-CLIENT-1/2/5: a client without a requested scope gets all of its scopes, for its group's audience")]
    public async Task ClientCredentials_IssueATokenForTheClientsGroup()
    {
        var response = await _auth.RequestTokenAsync(CatalogForm());

        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        var token = response.ShouldHaveData();
        token.TokenType.Should().Be("Bearer");
        token.ExpiresIn.Should().BePositive();
        token.Scope.Split(' ').Should().BeEquivalentTo("catalog.read", "catalog.write", "catalog.admin");

        var claims = JwtPayload(token.AccessToken);
        claims.GetProperty("client_id").GetString().Should().Be(_catalog.ClientId);
        claims.GetProperty("aud").GetString().Should().Be("catalog");
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("R-CLIENT-2: a requested subset of the client's scopes narrows the token")]
    public async Task RequestedScope_NarrowsTheToken()
    {
        var response = await _auth.RequestTokenAsync(CatalogForm(scope: "catalog.read"));

        response.ShouldHaveStatusCode(HttpStatusCode.OK);
        response.ShouldHaveData().Scope.Should().Be("catalog.read");
        JwtPayload(response.Data!.AccessToken).GetProperty("scope").GetString().Should().Be("catalog.read");
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("R-CLIENT-1: a wrong client secret is rejected with 401 invalid_client")]
    public async Task WrongSecret_IsRejectedAsInvalidClient()
    {
        var form = CatalogForm();
        form["client_secret"] = "not-the-secret";

        var response = await _auth.RequestTokenAsync(form);

        response.ShouldHaveStatusCode(HttpStatusCode.Unauthorized);
        Error(response).Error.Should().Be("invalid_client");
    }

    [Test]
    [AllureSeverity(SeverityLevel.critical)]
    [AllureDescription("R-CLIENT-2: a scope of another group is rejected with 400 invalid_scope")]
    public async Task ScopeOfAnotherGroup_IsRejectedAsInvalidScope()
    {
        var response = await _auth.RequestTokenAsync(CatalogForm(scope: "catalog.read orders.write"));

        response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        var error = Error(response);
        error.Error.Should().Be("invalid_scope");
        error.ErrorDescription.Should().Contain("orders.write");
    }

    [Test]
    [AllureSeverity(SeverityLevel.normal)]
    [AllureDescription("R-CLIENT-1: grant types other than client_credentials are rejected with 400 unsupported_grant_type")]
    public async Task PasswordGrant_IsNotSupported()
    {
        var form = CatalogForm();
        form["grant_type"] = "password";

        var response = await _auth.RequestTokenAsync(form);

        response.ShouldHaveStatusCode(HttpStatusCode.BadRequest);
        Error(response).Error.Should().Be("unsupported_grant_type");
    }

    private static OAuthError Error(RestResponse response) =>
        JsonSerializer.Deserialize<OAuthError>(response.Content!)!;

    /// <summary>Claims of a JWT (payload only — the signature is the services' business).</summary>
    private static JsonElement JwtPayload(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement.Clone();
    }
}
