using Butterfly.Client.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Ocelot.Configuration.File;
using Ocelot.DependencyInjection;
using Ocelot.Testing;
using Ocelot.Tracing.Butterfly;
using System.Net;
using TestStack.BDDfy;
using Xunit.Abstractions;

namespace Ocelot.Tracing.Butterfly.Acceptance;

public sealed class ButterflyTracingTests : AcceptanceSteps
{
    private int _butterflyCalled;
    private readonly ITestOutputHelper _output;

    private static readonly FileHttpHandlerOptions UseTracing = new()
    {
        UseTracing = true,
    };

    public ButterflyTracingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Should_forward_tracing_information_from_ocelot_and_downstream_services()
    {
        var port1 = PortFinder.GetRandomPort();
        var port2 = PortFinder.GetRandomPort();
        var route1 = GivenRoute(port1, "/api001/values", "/api/values");
        var route2 = GivenRoute(port2, "/api002/values", "/api/values");
        route1.HttpHandlerOptions = route2.HttpHandlerOptions = UseTracing;
        var configuration = GivenConfiguration(route1, route2);
        var butterflyPort = PortFinder.GetRandomPort();
        this.Given(x => GivenFakeButterfly(butterflyPort))
            .And(x => GivenServiceIsRunning(port1, "/api/values", HttpStatusCode.OK, "Hello from Laura", butterflyPort, "Service One"))
            .And(x => GivenServiceIsRunning(port2, "/api/values", HttpStatusCode.OK, "Hello from Tom", butterflyPort, "Service One"))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => x.GivenOcelotIsRunningUsingButterfly(butterflyPort))
            .When(x => WhenIGetUrlOnTheApiGateway("/api001/values"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
            .When(x => WhenIGetUrlOnTheApiGateway("/api002/values"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Tom"))
            .BDDfy();

        var commandOnAllStateMachines = Wait.For(10_000).Until(() => _butterflyCalled >= 4);
        _output.WriteLine($"_butterflyCalled is {_butterflyCalled}");
        Assert.True(commandOnAllStateMachines);
    }

    [Fact]
    public void Should_return_tracing_header()
    {
        var port = PortFinder.GetRandomPort();
        var route = GivenRoute(port, "/api001/values", "/api/values");
        route.HttpHandlerOptions = UseTracing;
        route.DownstreamHeaderTransform = new Dictionary<string, string>
        {
            {"Trace-Id", "{TraceId}"},
            {"Tom", "Laura"},
        };
        var configuration = GivenConfiguration(route);
        var butterflyPort = PortFinder.GetRandomPort();
        this.Given(x => GivenFakeButterfly(butterflyPort))
            .And(x => GivenServiceIsRunning(port, "/api/values", HttpStatusCode.OK, "Hello from Laura", butterflyPort, "Service One"))
            .And(x => GivenThereIsAConfiguration(configuration))
            .And(x => x.GivenOcelotIsRunningUsingButterfly(butterflyPort))
            .When(x => WhenIGetUrlOnTheApiGateway("/api001/values"))
            .Then(x => ThenTheStatusCodeShouldBe(HttpStatusCode.OK))
            .And(x => ThenTheResponseBodyShouldBe("Hello from Laura"))
            .And(x => ThenTheResponseHeaderExists("Trace-Id"))
            .And(x => ThenTheResponseHeaderIs("Tom", "Laura"))
            .BDDfy();
    }

    private int GivenOcelotIsRunningUsingButterfly(int butterflyPort)
    {
        void WithButterfly(IServiceCollection services) => services
            .AddOcelot()
            .AddButterfly(option =>
            {
                option.CollectorUrl = DownstreamUrl(butterflyPort); // this is the url that the butterfly collector server is running on...
                option.Service = "Ocelot";
            });
        return GivenOcelotIsRunning(WithButterfly);
    }

    private void GivenFakeButterfly(int port)
    {
        Task Map(HttpContext context)
        {
            _butterflyCalled++;
            return context.Response.WriteAsync("OK...");
        }
        handler.GivenThereIsAServiceRunningOn(port, Map);
    }

    private string GivenServiceIsRunning(int port, string basePath, HttpStatusCode statusCode, string responseBody, int butterflyPort, string serviceName)
    {
        string? downstreamPath = string.Empty;
        void WithButterfly(IServiceCollection services)
        {
            services.AddButterfly(option =>
            {
                option.CollectorUrl = DownstreamUrl(butterflyPort);
                option.Service = serviceName;
                option.IgnoredRoutesRegexPatterns = [];
            });
        }
        Task MapStatusAndPath(HttpContext context)
        {
            downstreamPath = !string.IsNullOrEmpty(context.Request.PathBase.Value) ? context.Request.PathBase.Value : context.Request.Path.Value;
            bool oK = downstreamPath == basePath;
            context.Response.StatusCode = oK ? (int)statusCode : (int)HttpStatusCode.NotFound;
            return context.Response.WriteAsync(oK ? responseBody : "downstream path didnt match base path");
        }
        handler.GivenThereIsAServiceRunningOn(DownstreamUrl(port), basePath, WithButterfly, MapStatusAndPath);
        return downstreamPath;
    }
}
