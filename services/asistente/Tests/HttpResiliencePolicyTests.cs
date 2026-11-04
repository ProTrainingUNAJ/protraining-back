using System.Net;
using API.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace Asistente.Tests;

public sealed class HttpResiliencePolicyTests
{
    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responseFactory(request));
        }
    }

    [Fact]
    public void ConfigureProTrainingResilienceOptions_Sets_Expected_Parameters()
    {
        // Arrange
        var options = new HttpStandardResilienceOptions();

        // Act
        HttpResilienceExtensions.ConfigureProTrainingResilienceOptions(options);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.TotalRequestTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CircuitBreaker.SamplingDuration);
        Assert.Equal(5, options.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CircuitBreaker.BreakDuration);
        Assert.True(options.CircuitBreaker.FailureRatio >= 0.5);
        Assert.True(options.Retry.UseJitter);
        Assert.Equal(Polly.DelayBackoffType.Exponential, options.Retry.BackoffType);
    }

    [Fact]
    public async Task Given_FailingDownstream_When_FiveFailuresAccumulate_Then_CircuitBreaker_Opens_And_Cuts_Subsequent_Calls_Fast()
    {
        // Arrange: Handler que siempre devuelve 500 Internal Server Error
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var services = new ServiceCollection();
        services.AddHttpClient("TestClient")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(options =>
            {
                HttpResilienceExtensions.ConfigureProTrainingResilienceOptions(options);
                // Para que el test corra rápido y no espere 1s/2s en cada retry, configuramos delays mínimos en retry
                options.Retry.Delay = TimeSpan.FromMilliseconds(5);
            });

        var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient("TestClient");

        // Act: Enviar peticiones hasta que el breaker se abra
        // Con MinimumThroughput = 5 y FailureRatio = 0.9, al alcanzar 5 fallos el breaker pasa a Open.
        // Dado que Retry hace reintentos, el primer request puede hacer hasta 4 intentos (1 inicial + 3 reintentos).
        // El segundo request alcanza el 5to intento fallido y abre el breaker.
        var circuitBroken = false;
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var response = await client.GetAsync("http://dummy-internal-service/api/test");
            }
            catch (BrokenCircuitException)
            {
                circuitBroken = true;
                break;
            }
        }

        // Assert
        Assert.True(circuitBroken, "El Circuit Breaker debió abrirse tras acumular al menos 5 fallos seguidos.");

        // Verificar que con el circuito abierto, una llamada subsiguiente falla DE INMEDIATO con BrokenCircuitException
        // y NO incrementa las llamadas reales al handler (fail-fast)
        var callsBeforeFastFail = handler.CallCount;
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("http://dummy-internal-service/api/test"));
        Assert.Equal(callsBeforeFastFail, handler.CallCount);
    }

    [Fact]
    public async Task Given_SlowDownstream_When_ExceedingAttemptTimeout_Then_Throws_TimeoutRejectedException()
    {
        // Arrange: Handler que tarda 200ms
        var services = new ServiceCollection();
        services.AddHttpClient("TimeoutClient")
            .ConfigurePrimaryHttpMessageHandler(() => new DelayingHandler(TimeSpan.FromMilliseconds(200)))
            .AddStandardResilienceHandler(options =>
            {
                HttpResilienceExtensions.ConfigureProTrainingResilienceOptions(options);
                // Configuramos un AttemptTimeout ultra-corto para verificar que la estrategia corta por TimeoutRejectedException
                options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(50);
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            });

        var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient("TimeoutClient");

        // Act & Assert: Debe ser abortado por TimeoutRejectedException de Polly
        await Assert.ThrowsAsync<TimeoutRejectedException>(() => client.GetAsync("http://dummy-internal-service/api/slow"));
    }

    private sealed class DelayingHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
