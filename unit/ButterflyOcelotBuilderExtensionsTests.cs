using Butterfly.Client;
using Butterfly.Client.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Ocelot.DependencyInjection;
using Ocelot.Logging;
using Ocelot.Testing;
using System.Runtime.CompilerServices;

namespace Ocelot.Tracing.Butterfly.UnitTests;

public class ButterflyOcelotBuilderExtensionsTests : Unit
{
    private IHostingEnvironment GetHostingEnvironment([CallerMemberName] string? testName = null)
    {
        testName ??= TestName();
        var environment = new Mock<IHostingEnvironment>();
        environment.Setup(e => e.ApplicationName).Returns(testName);
        environment.Setup(e => e.EnvironmentName).Returns(testName);
        return environment.Object;
    }

    [Fact]
    public void AddButterfly_IOcelotBuilder()
    {
        // Arrange
        IConfiguration configRoot = new ConfigurationRoot([]);
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(GetHostingEnvironment());
        services.AddSingleton(configRoot);
        IOcelotBuilder builder = services.AddOcelot(configRoot);
        ButterflyOptions options = new();
        static void settings(ButterflyOptions o) => o.CollectorUrl = "https://ocelot.net";

        // Act
        var actual = builder.AddButterfly(settings);

        // Assert
        Assert.Same(builder, actual);
        ServiceLifetime lifetime = services
            .Single(x => x.ServiceType == typeof(IOcelotTracer))
            .Lifetime;
        Assert.Equal(ServiceLifetime.Singleton, lifetime);
        lifetime = services
            .Single(x => x.ServiceType == typeof(IButterflyDispatcherProvider))
            .Lifetime;
        Assert.Equal(ServiceLifetime.Singleton, lifetime);
        lifetime = services
            .Single(x => x.ServiceType == typeof(IConfigureOptions<ButterflyOptions>))
            .Lifetime;
        Assert.Equal(ServiceLifetime.Singleton, lifetime);

        var provider = services.BuildServiceProvider(true);
        var actualTracer = provider.GetService<IOcelotTracer>();
        Assert.NotNull(actualTracer);
        Assert.IsType<ButterflyTracer>(actualTracer);

        var actualProvider = provider.GetService<IButterflyDispatcherProvider>();
        Assert.NotNull(actualProvider);
        Assert.IsType<ButterflyDispatcherProvider>(actualProvider);

        var actualOptions = provider.GetService<IConfigureOptions<ButterflyOptions>>();
        Assert.NotNull(actualOptions);
        Assert.IsType<ConfigureNamedOptions<ButterflyOptions>>(actualOptions);
    }
}
