using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace API.Extensions;

/// <summary>
/// Estandarización de políticas de resiliencia HTTP con Polly v8 (KAN-433 #138).
/// Provee:
/// - Timeout por intento individual de 5 segundos.
/// - Circuit Breaker que abre tras 5 fallos seguidos y corta llamadas durante 30 segundos.
/// - Reintentos con Backoff Exponencial y Jitter (hasta 3 intentos) para evitar tormentas de reintentos.
/// - Timeout total de solicitud de 30 segundos.
/// </summary>
public static class HttpResilienceExtensions
{
    public static IHttpStandardResiliencePipelineBuilder AddProTrainingResilience(this IHttpClientBuilder builder)
    {
        return builder.AddStandardResilienceHandler(ConfigureProTrainingResilienceOptions);
    }

    public static void ConfigureProTrainingResilienceOptions(HttpStandardResilienceOptions options)
    {
        // 1. Timeout por intento individual: 5 segundos
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);

        // 2. Timeout total para la operación completa (incluye reintentos): 30 segundos
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);

        // 3. Circuit Breaker: si acumula 5 errores en la ventana de 30s, abre durante 30s
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.FailureRatio = 0.9;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

        // 4. Reintentos con backoff exponencial y jitter
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
    }
}

