using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Auth;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Configuration;
using ApiTestFramework.Core.DI;
using Autofac;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Framework.Tests;

/// <summary>
/// Named auth clients: one shared identity provider, a dedicated OAuth2 client per group of
/// services. Runs against an in-process stub that is both the token endpoint and the API.
/// </summary>
[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Pluggable Authentication")]
[Category("Unit")]
[NonParallelizable] // AuthenticationContext is process-wide
public class AuthClientTests : BaseTest
{
    private IAuthenticationProvider _originalProvider = null!;
    private IdpAndApiStub _stub = null!;

    protected override void OnFixtureSetUp()
    {
        _originalProvider = AuthenticationContext.Provider;
    }

    [SetUp]
    public void StartStub() => _stub = new IdpAndApiStub();

    [TearDown]
    public void RestoreAuthentication()
    {
        _stub.Dispose();
        AuthenticationContext.Reset();
        AuthenticationContext.Provider = _originalProvider;
    }

    private IConfiguration Config(params (string Key, string Value)[] values)
    {
        var pairs = new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = "OAuth2ClientCredentials",
            ["Authentication:OAuth2:TokenUrl"] = _stub.BaseUrl + "connect/token",
        };
        foreach (var (key, value) in values)
            pairs[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    private IConfiguration TwoGroups() => Config(
        ("Authentication:Clients:Orders:OAuth2:ClientId", "orders-api-tests"),
        ("Authentication:Clients:Orders:OAuth2:ClientSecret", "orders-secret"),
        ("Authentication:Clients:Orders:OAuth2:Scope", "orders.read orders.write"),
        ("Authentication:Clients:Orders:OAuth2:Audience", "orders"),
        ("Authentication:Clients:Catalog:OAuth2:ClientId", "catalog-api-tests"),
        ("Authentication:Clients:Catalog:OAuth2:ClientSecret", "catalog-secret"),
        ("Authentication:Clients:Catalog:OAuth2:Scope", "catalog.read"));

    private static void RegisterClients(IConfiguration configuration, params string[] clients)
    {
        foreach (var client in clients)
            AuthenticationContext.SetClient(client, AuthenticationProviderFactory.CreateForClient(configuration, client));
    }

    private ApiClient Service(string name, string? authClient) =>
        new(_stub.BaseUrl, name) { AuthClient = authClient };

    private static string? Header(RestRequest request, string name) =>
        request.Parameters
            .FirstOrDefault(p => p.Type == ParameterType.HttpHeader
                                 && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Value?.ToString();

    [Test]
    public void ClientOptions_InheritSharedSettings_ButNeverCredentials()
    {
        var configuration = Config(
            ("Authentication:Scheme", "Token"),
            ("Authentication:OAuth2:ClientId", "default-client"),
            ("Authentication:OAuth2:ClientSecret", "default-secret"),
            ("Authentication:OAuth2:Audience", "shared-audience"),
            ("Authentication:Token", "default-token"),
            ("Authentication:Clients:Catalog:OAuth2:ClientId", "catalog-api-tests"),
            ("Authentication:Clients:Legacy:Mode", "ApiKey"),
            ("Authentication:Clients:Legacy:HeaderName", "X-Api-Key"));

        var catalog = AuthenticationProviderFactory.BindClientOptions(configuration, "Catalog");
        catalog.Mode.Should().Be("OAuth2ClientCredentials");
        catalog.Scheme.Should().Be("Token");
        catalog.OAuth2.TokenUrl.Should().Be(_stub.BaseUrl + "connect/token");
        catalog.OAuth2.Audience.Should().Be("shared-audience");
        catalog.OAuth2.ClientId.Should().Be("catalog-api-tests");
        catalog.OAuth2.ClientSecret.Should().BeEmpty("a client never inherits another client's secret");
        catalog.Token.Should().BeEmpty();

        var legacy = AuthenticationProviderFactory.BindClientOptions(configuration, "legacy");
        legacy.Mode.Should().Be("ApiKey");
        legacy.HeaderName.Should().Be("X-Api-Key");
    }

    [Test]
    public void UnknownClient_FailsWithTheConfigurationKeyToAdd()
    {
        var act = () => AuthenticationProviderFactory.CreateForClient(TwoGroups(), "Billing");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Billing'*Authentication:Clients:Billing*TEST_Authentication__Clients__Billing__*");
    }

    [Test]
    public async Task EachServiceSendsItsGroupsToken_AndAGroupFetchesOneToken()
    {
        RegisterClients(TwoGroups(), "Orders", "Catalog");
        using var orders = Service("OrderService", "Orders");
        using var shipping = Service("ShippingService", "Orders");
        using var products = Service("ProductService", "Catalog");

        var ordersAuth = (await orders.SendAsync(Echo())).Content;
        var shippingAuth = (await shipping.SendAsync(Echo())).Content;
        var productsAuth = (await products.SendAsync(Echo())).Content;
        await orders.SendAsync(Echo());

        ordersAuth.Should().Be("Bearer token-orders-api-tests-1");
        shippingAuth.Should().Be(ordersAuth, "services of one group share the client and its cached token");
        productsAuth.Should().Be("Bearer token-catalog-api-tests-2");

        _stub.TokenRequests.Should().HaveCount(2);
        _stub.TokenRequests.Should().ContainSingle(r => r["client_id"] == "orders-api-tests")
            .Which.Should().Contain(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_secret"] = "orders-secret",
                ["scope"] = "orders.read orders.write",
                ["audience"] = "orders",
            });
        _stub.TokenRequests.Should().ContainSingle(r => r["client_id"] == "catalog-api-tests")
            .Which.Should().NotContainKey("audience");
    }

    [Test]
    public async Task ServiceWithoutClient_UsesTheDefaultProvider()
    {
        AuthenticationContext.Provider = new StaticTokenAuthenticationProvider("default-token");
        using var service = Service("PublicService", authClient: null);

        var response = await service.SendAsync(Echo());

        response.Content.Should().Be("Bearer default-token");
        _stub.TokenRequests.Should().BeEmpty();
    }

    [Test]
    public void AsClient_UsesAnotherGroupsCredentials_ForCrossGroupChecks()
    {
        AuthenticationContext.SetClient("Orders", new StaticTokenAuthenticationProvider("orders-token"));
        AuthenticationContext.SetClient("Catalog", new StaticTokenAuthenticationProvider("catalog-token"));

        var request = Echo().AsClient("Orders").Build(defaultAuthClient: "Catalog");

        Header(request, "Authorization").Should().Be("Bearer orders-token");
    }

    [Test]
    public void IdentityExplicitHeaderAndWithoutAuth_StillWinOverTheServiceClient()
    {
        AuthenticationContext.SetClient("Catalog", new StaticTokenAuthenticationProvider("catalog-token"));
        TokenProvider.SetToken("auth-client-test-user", "user-token");
        try
        {
            Header(Echo().AsIdentity("auth-client-test-user").Build("Catalog"), "Authorization")
                .Should().Be("Bearer user-token");
            Header(Echo().WithBearerToken("explicit").Build("Catalog"), "Authorization")
                .Should().Be("Bearer explicit");
            Header(Echo().WithoutAuth().Build("Catalog"), "Authorization")
                .Should().BeNull();
            Header(Echo().AsClient("Catalog").AsIdentity("auth-client-test-user").Build(), "Authorization")
                .Should().Be("Bearer user-token", "the last auth-related call wins");
        }
        finally
        {
            TokenProvider.Remove("auth-client-test-user");
        }
    }

    [Test]
    public void ClientWithoutConfiguration_FailsOnFirstRequest()
    {
        var act = () => Echo().Build(defaultAuthClient: "NotConfigured");

        act.Should().Throw<InvalidOperationException>().WithMessage("*'NotConfigured'*");
    }

    [Test]
    public void RejectedClient_ReportsTheIdpError()
    {
        RegisterClients(Config(
            ("Authentication:Clients:Orders:OAuth2:ClientId", "orders-api-tests"),
            ("Authentication:Clients:Orders:OAuth2:ClientSecret", "wrong")), "Orders");

        var act = () => Echo().Build("Orders");

        act.Should().Throw<InvalidOperationException>().WithMessage("*HTTP 401*invalid_client*");
    }

    [Test]
    public void ServicesSection_WiresTheAuthClientIntoTheRegisteredApiClient()
    {
        var config = new TestConfiguration
        {
            Services =
            {
                ["OrderService"] = new ServiceConfig { BaseUrl = _stub.BaseUrl, AuthClient = "Orders" },
                ["HealthService"] = new ServiceConfig { BaseUrl = _stub.BaseUrl },
            },
        };
        var builder = new ContainerBuilder();
        builder.RegisterModule(new ApiClientModule(config));
        using var container = builder.Build();

        container.ResolveNamed<ApiClient>("OrderService").AuthClient.Should().Be("Orders");
        container.ResolveNamed<ApiClient>("HealthService").AuthClient.Should().BeNull();
    }

    private static RequestBuilder Echo() =>
        RequestBuilder.Create().WithMethod(Method.Get).WithPath("/echo-authorization")
            .WithoutResponseValidation();

    /// <summary>
    /// POST /connect/token: client-credentials grant (known clients only, numbered tokens);
    /// any other request: echoes the Authorization header it received as text/plain.
    /// </summary>
    private sealed class IdpAndApiStub : IDisposable
    {
        private static readonly Dictionary<string, string> Secrets = new()
        {
            ["orders-api-tests"] = "orders-secret",
            ["catalog-api-tests"] = "catalog-secret",
        };

        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<Dictionary<string, string>> _tokenRequests = new();
        private int _issued;

        public IdpAndApiStub()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            BaseUrl = $"http://localhost:{((IPEndPoint)probe.LocalEndpoint).Port}/";
            probe.Stop();
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string BaseUrl { get; }
        public IReadOnlyList<Dictionary<string, string>> TokenRequests => _tokenRequests.ToArray();

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                var (status, contentType, body) = context.Request.Url!.AbsolutePath == "/connect/token"
                    ? await IssueTokenAsync(context.Request)
                    : (200, "text/plain", context.Request.Headers["Authorization"] ?? "");

                context.Response.StatusCode = status;
                context.Response.ContentType = contentType;
                var bytes = Encoding.UTF8.GetBytes(body);
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        private async Task<(int, string, string)> IssueTokenAsync(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var form = HttpUtility.ParseQueryString(await reader.ReadToEndAsync());
            var fields = form.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => form[k]!);
            _tokenRequests.Enqueue(fields);

            var clientId = fields.GetValueOrDefault("client_id") ?? "";
            if (!Secrets.TryGetValue(clientId, out var secret) || fields.GetValueOrDefault("client_secret") != secret)
                return (401, "application/json", """{"error":"invalid_client"}""");

            var token = $"token-{clientId}-{Interlocked.Increment(ref _issued)}";
            return (200, "application/json", $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":3600}""");
        }

        public void Dispose() => _listener.Close();
    }
}
