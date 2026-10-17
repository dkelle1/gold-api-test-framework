using System.Net;
using System.Net.Sockets;
using System.Text;
using Allure.NUnit.Attributes;
using ApiTestFramework.Contracts;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Framework.Tests;

/// <summary>
/// Contract validation wired into ApiClient (automatic modes), the explicit assertion and the
/// coverage report — against an in-process HTTP stub, no services.
/// </summary>
[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Contract Validation")]
[Category("Unit")]
[NonParallelizable] // ContractValidation and ResponseValidators are process-wide
public class ContractValidationIntegrationTests : BaseTest
{
    private const string Widget =
        """{"id":1,"name":"Bolt","status":1,"color":"blue","price":1,"createdAt":"2026-01-01T00:00:00Z","reference":"0d1d5e1c-0000-4000-8000-000000000001","owner":null,"note":null,"parts":[]}""";

    private RoutedJsonStub _stub = null!;

    protected override void OnFixtureSetUp()
    {
        _stub = new RoutedJsonStub(new Dictionary<string, (int, string)>
        {
            ["GET /api/widgets/1"] = (200, Widget),
            ["GET /api/widgets/2"] = (200, """{"id":2,"name":"Nut","status":"Shipped"}"""),   // drifted
            ["GET /api/widgets/404"] = (404, """{"message":"Widget 404 not found"}"""),
            ["DELETE /api/widgets/1"] = (204, ""),
        });
    }

    public override void OneTimeTearDown()
    {
        _stub.Dispose();
        base.OneTimeTearDown();
    }

    [TearDown]
    public void DisableContracts() => ContractValidation.Disable();

    private static void Enable(ContractValidationMode mode) => ContractValidation.Enable(new ContractValidationOptions
    {
        Mode = mode,
        Specs = { ["Widgets"] = "Fixtures/widgets-contract.json" }
    });

    private ApiClient Client(string name = "Widgets") => new(_stub.BaseUrl, name);

    private static RequestBuilder GetWidget(int id)
        => RequestBuilder.Create().WithMethod(Method.Get).WithPath("/api/widgets/{id}").WithPathSegment("id", id);

    [Test]
    public async Task FailMode_ConformingResponses_PassAndAreCounted()
    {
        Enable(ContractValidationMode.Fail);
        using var client = Client();

        (await client.SendAsync(GetWidget(1))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.SendAsync(GetWidget(404))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        ContractValidation.Coverage.TotalViolations.Should().Be(0);
    }

    [Test]
    public async Task FailMode_ViolationFailsAtTheRequest_WithEveryViolationListed()
    {
        Enable(ContractValidationMode.Fail);
        using var client = Client();

        var act = () => client.SendAsync(GetWidget(2));

        var failure = await act.Should().ThrowAsync<AssertionException>();
        failure.Which.Message.Should()
            .Contain("GET /api/widgets/{id} (/api/widgets/2) → 200 [Widgets]")
            .And.Contain("$.status: value 'Shipped' not in enum [0, 1, 2]")
            .And.Contain("$.price: non-nullable property is missing");
    }

    [Test]
    public async Task WarnMode_RecordsViolationsWithoutFailing()
    {
        Enable(ContractValidationMode.Warn);
        using var client = Client();

        var response = await client.SendAsync(GetWidget(2));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ContractValidation.Coverage.TotalViolations.Should().Be(1);
    }

    [Test]
    public async Task WithoutResponseValidation_SkipsTheCheck()
    {
        Enable(ContractValidationMode.Fail);
        using var client = Client();

        var response = await client.SendAsync(GetWidget(2).WithoutResponseValidation());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ServicesWithoutAContract_AreNotChecked()
    {
        Enable(ContractValidationMode.Fail);
        using var other = Client("SomeOtherService");

        var response = await other.SendAsync(GetWidget(2));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task OffMode_ExplicitAssertionStillWorks()
    {
        Enable(ContractValidationMode.Off);
        using var client = Client();

        var good = await client.SendAsync<object>(GetWidget(1));
        good.ShouldMatchContract().Should().BeSameAs(good);

        var drifted = await client.SendAsync(GetWidget(2));     // Off: no automatic failure
        var act = () => drifted.ShouldMatchContract("Widgets");
        act.Should().Throw<AssertionException>().WithMessage("*$.status*");
    }

    [Test]
    public void ExplicitAssertion_RequiresEnabledContracts()
    {
        var response = new RestResponse(new RestRequest("/api/widgets/1"));

        var act = () => response.ShouldMatchContract();

        act.Should().Throw<InvalidOperationException>().WithMessage("*EnableFromConfiguration*");
    }

    [Test]
    public async Task CoverageReport_ListsExercisedMissingAndViolatingResponses()
    {
        Enable(ContractValidationMode.Warn);
        using var client = Client();
        await client.SendAsync(GetWidget(1));
        await client.SendAsync(GetWidget(2));
        await client.SendAsync(RequestBuilder.Create().WithMethod(Method.Delete).WithPath("/api/widgets/{id}").WithPathSegment("id", 1));

        var directory = Path.Combine(Path.GetTempPath(), "contract-coverage-" + Guid.NewGuid().ToString("N"));
        var markdown = File.ReadAllText(ContractValidation.WriteCoverageReport(directory)!);

        markdown.Should().Contain("| Widgets | `GET /api/widgets/{id}` | 200 | 2 | 1 |");
        markdown.Should().Contain("| Widgets | `DELETE /api/widgets/{id}` | 204 | 1 | 0 |");
        markdown.Should().Contain("| Widgets | `GET /api/widgets/{id}` | 404 | 0 | 0 | not exercised |");
        File.Exists(Path.Combine(directory, "contract-coverage.json")).Should().BeTrue();
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>Serves fixed (status, JSON body) pairs keyed by "METHOD /path".</summary>
    private sealed class RoutedJsonStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly IReadOnlyDictionary<string, (int Status, string Body)> _routes;

        public RoutedJsonStub(IReadOnlyDictionary<string, (int, string)> routes)
        {
            _routes = routes;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            BaseUrl = $"http://localhost:{((IPEndPoint)probe.LocalEndpoint).Port}/";
            probe.Stop();
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string BaseUrl { get; }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                var key = $"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}";
                var (status, body) = _routes.TryGetValue(key, out var route) ? route : (500, "");
                context.Response.StatusCode = status;
                if (body.Length > 0)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                context.Response.Close();
            }
        }

        public void Dispose() => _listener.Close();
    }
}
