using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Client;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace ApiTestFramework.Framework.Tests;

/// <summary>
/// Unit tests for ApiClient's retry policy against a local in-process HTTP stub —
/// no live services needed.
/// </summary>
[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("ApiClient Retry")]
[Category("Unit")]
public class ApiClientRetryTests : BaseTest
{
    [Test]
    public async Task Get_RetriesServerErrors_UntilSuccess()
    {
        using var stub = new ScriptedHttpStub(503, 502, 200);
        using var client = new ApiClient(stub.BaseUrl, "Stub") { RetryCount = 3 };

        var response = await client.SendAsync(RequestFactory.Get("/resource"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.Hits.Should().Be(3);
    }

    [Test]
    public async Task Get_StopsAfterRetryCount_AndReturnsLastResponse()
    {
        using var stub = new ScriptedHttpStub(503, 503, 503, 200);
        using var client = new ApiClient(stub.BaseUrl, "Stub") { RetryCount = 2 };

        var response = await client.SendAsync(RequestFactory.Get("/resource"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        stub.Hits.Should().Be(3);
    }

    [Test]
    public async Task Get_WithDefaultRetryCount_IsNotRetried()
    {
        using var stub = new ScriptedHttpStub(503, 200);
        using var client = new ApiClient(stub.BaseUrl, "Stub");

        var response = await client.SendAsync(RequestFactory.Get("/resource"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        stub.Hits.Should().Be(1);
    }

    [Test]
    public async Task ClientErrors_AreNeverRetried()
    {
        using var stub = new ScriptedHttpStub(404, 200);
        using var client = new ApiClient(stub.BaseUrl, "Stub") { RetryCount = 3 };

        var response = await client.SendAsync(RequestFactory.Get("/resource"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        stub.Hits.Should().Be(1);
    }

    [Test]
    public async Task Post_ServerError_IsNotRetried_ToAvoidDuplicates()
    {
        using var stub = new ScriptedHttpStub(500, 201);
        using var client = new ApiClient(stub.BaseUrl, "Stub") { RetryCount = 3 };

        var response = await client.SendAsync(RequestFactory.Post("/resource", new { Name = "x" }));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        stub.Hits.Should().Be(1);
    }

    [Test]
    public async Task Post_WithoutAnyResponse_IsRetried()
    {
        // Accepts the TCP connection and closes it immediately: the request
        // never produced an HTTP response, so even a POST is safe to retry.
        using var stub = new ClosingTcpStub();
        using var client = new ApiClient(stub.BaseUrl, "Stub") { RetryCount = 2 };

        var response = await client.SendAsync(RequestFactory.Post("/resource", new { Name = "x" }));

        ((int)response.StatusCode).Should().Be(0);
        stub.Connections.Should().Be(3);
    }

    /// <summary>Returns the scripted status codes in order (the last one repeats).</summary>
    private sealed class ScriptedHttpStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<int> _statuses;
        private readonly int _fallbackStatus;
        private int _hits;

        public ScriptedHttpStub(params int[] statuses)
        {
            _statuses = new ConcurrentQueue<int>(statuses);
            _fallbackStatus = statuses[^1];
            BaseUrl = $"http://localhost:{FreePort()}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string BaseUrl { get; }
        public int Hits => Volatile.Read(ref _hits);

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; } // listener stopped

                Interlocked.Increment(ref _hits);
                context.Response.StatusCode = _statuses.TryDequeue(out var status) ? status : _fallbackStatus;
                context.Response.Close();
            }
        }

        public void Dispose() => _listener.Close();
    }

    /// <summary>Accepts TCP connections and closes them without sending a response.</summary>
    private sealed class ClosingTcpStub : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private int _connections;

        public ClosingTcpStub()
        {
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _ = Task.Run(ServeAsync);
        }

        public string BaseUrl { get; }
        public int Connections => Volatile.Read(ref _connections);

        private async Task ServeAsync()
        {
            while (true)
            {
                TcpClient socket;
                try { socket = await _listener.AcceptTcpClientAsync(); }
                catch (Exception) { return; } // listener stopped

                Interlocked.Increment(ref _connections);
                socket.Client.LingerState = new LingerOption(true, 0); // RST instead of FIN
                socket.Close();
            }
        }

        public void Dispose() => _listener.Stop();
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
