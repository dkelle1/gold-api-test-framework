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
        // Every service from the "Services" section gets a named client
        foreach (var (key, service) in _config.Services)
        {
            RegisterClient(builder, key, service, _config.RetryCount, _config.DefaultTimeoutSeconds);
        }

        builder.RegisterInstance(_config).AsSelf().SingleInstance();
    }

    private static void RegisterClient(ContainerBuilder builder, string key, ServiceConfig service,
        int retryCount, int timeoutSeconds)
    {
        if (string.IsNullOrEmpty(service.BaseUrl)) return;

        var name = string.IsNullOrEmpty(service.Name) ? key : service.Name;
        builder.Register(c => new ApiClient(service.BaseUrl, name, timeoutSeconds)
            {
                RetryCount = retryCount,
                AuthClient = string.IsNullOrEmpty(service.AuthClient) ? null : service.AuthClient,
            })
            .Named<ApiClient>(key)
            .SingleInstance();
    }
}
