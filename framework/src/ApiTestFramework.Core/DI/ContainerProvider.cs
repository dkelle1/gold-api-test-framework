using Autofac;

namespace ApiTestFramework.Core.DI;

/// <summary>
/// Provides a global Autofac container for the test framework.
/// Modules are registered during test assembly setup.
/// </summary>
public static class ContainerProvider
{
    private static IContainer? _container;

    public static IContainer Container =>
        _container ?? throw new InvalidOperationException(
            "Container has not been initialized. Call Initialize() in [SetUpFixture].");

    public static void Initialize(Action<ContainerBuilder> configure)
    {
        var builder = new ContainerBuilder();
        configure(builder);
        _container = builder.Build();
    }

    public static T Resolve<T>() where T : notnull
        => Container.Resolve<T>();

    public static T ResolveNamed<T>(string name) where T : notnull
        => Container.ResolveNamed<T>(name);

    public static void Dispose()
    {
        _container?.Dispose();
        _container = null;
    }
}
