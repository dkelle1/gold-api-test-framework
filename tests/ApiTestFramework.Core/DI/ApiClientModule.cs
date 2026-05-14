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
        foreach (var (name, serviceConfig) in _config.Services)
        {
            // Capture loop variables for the closure
            var serviceName = name;
            var baseUrl = serviceConfig.BaseUrl;
            var retryCount = _config.RetryCount;

            builder.Register(_ => new ApiClient(baseUrl, serviceName) { RetryCount = retryCount })
                .Named<ApiClient>(serviceName)
                .SingleInstance();
        }

        builder.RegisterInstance(_config).AsSelf().SingleInstance();
    }
}
