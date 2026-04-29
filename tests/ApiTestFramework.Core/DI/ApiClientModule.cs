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
        builder.Register(c => new ApiClient(_config.ProductService.BaseUrl, _config.ProductService.Name))
            .Named<ApiClient>("ProductService")
            .SingleInstance();

        builder.Register(c => new ApiClient(_config.OrderService.BaseUrl, _config.OrderService.Name))
            .Named<ApiClient>("OrderService")
            .SingleInstance();

        builder.Register(c => new ApiClient(_config.AuthService.BaseUrl, _config.AuthService.Name))
            .Named<ApiClient>("AuthService")
            .SingleInstance();

        builder.RegisterInstance(_config).AsSelf().SingleInstance();
    }
}
