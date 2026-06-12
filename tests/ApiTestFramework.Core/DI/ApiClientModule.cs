using Autofac;
using ApiTestFramework.Core.Client;
using ApiTestFramework.Core.Configuration;

namespace ApiTestFramework.Core.DI;

/// <summary>
/// Autofac module that registers API clients for all configured services.
/// </summary>
public class ApiClientModule : Module
{
    private readonly TestConfiguration _config;

    public ApiClientModule(TestConfiguration config)
    {
        _config = config;
    }

    protected override void Load(ContainerBuilder builder)
    {
        // Legacy typed sections (kept for backwards compatibility with the
        // demo microservices) — skipped when not configured.
        RegisterClient(builder, "ProductService", _config.ProductService);
        RegisterClient(builder, "OrderService", _config.OrderService);
        RegisterClient(builder, "AuthService", _config.AuthService);

        // Generic registry: any product registers its services under
        // "Services" in appsettings.test.json without touching this class.
        foreach (var (key, service) in _config.Services)
        {
            RegisterClient(builder, key, service);
        }

        builder.RegisterInstance(_config).AsSelf().SingleInstance();
    }

    private static void RegisterClient(ContainerBuilder builder, string key, ServiceConfig service)
    {
        if (string.IsNullOrEmpty(service.BaseUrl)) return;

        var name = string.IsNullOrEmpty(service.Name) ? key : service.Name;
        builder.Register(c => new ApiClient(service.BaseUrl, name))
            .Named<ApiClient>(key)
            .SingleInstance();
    }
}
