using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class AthletePurgeDispatcher(
    IExternalServicePurgeClient purgeClient,
    IConfiguration configuration,
    ILogger<AthletePurgeDispatcher> logger) : IAthletePurgeDispatcher
{
    private sealed record ServiceTarget(string Name, string BaseUrl);

    public async Task<AthletePurgeDispatchResult> DispatchPurgeToExternalServicesAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        var servicesToPurge = GetTargetServices();
        var allSucceeded = true;
        string? failureReason = null;

        foreach (var target in servicesToPurge)
        {
            var success = false;
            const int maxAttempts = 3; // Reintentos con retroceso exponencial ante fallos transitorios de red
            string? lastServiceError = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var result = await purgeClient.PurgeAthleteInServiceAsync(
                    target.Name,
                    target.BaseUrl,
                    athleteId,
                    cancellationToken);

                if (result.IsSuccess)
                {
                    success = true;
                    break;
                }

                lastServiceError = result.ErrorMessage;
                logger.LogWarning(
                    "Intento {Attempt}/{MaxAttempts} fallido al purgar atleta {AthleteId} en {ServiceName}. Motivo: {Error}",
                    attempt,
                    maxAttempts,
                    athleteId,
                    target.Name,
                    result.ErrorMessage);

                if (attempt < maxAttempts)
                {
                    // Retroceso exponencial: 150 ms, 300 ms...
                    var delayMs = (int)(150 * Math.Pow(2, attempt - 1));
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            if (!success)
            {
                allSucceeded = false;
                failureReason = $"El servicio {target.Name} falló tras {maxAttempts} intentos: {lastServiceError}";
                logger.LogWarning(
                    "El servicio {ServiceName} no pudo completar la purga del atleta {AthleteId} tras {MaxAttempts} intentos. Se aborta el ciclo para este atleta. Motivo: {Error}",
                    target.Name,
                    athleteId,
                    maxAttempts,
                    lastServiceError);
                break; // No seguimos con los restantes si uno falló, para garantizar consistencia y reintentar todo el bloque
            }
        }

        return new AthletePurgeDispatchResult(allSucceeded, allSucceeded ? null : failureReason);
    }

    private List<ServiceTarget> GetTargetServices()
    {
        return new List<ServiceTarget>
        {
            new("Identidad", configuration["Services:Identidad:BaseUrl"] ?? "http://identidad:8080"),
            new("Entrenamiento", configuration["Services:Entrenamiento:BaseUrl"] ?? "http://entrenamiento:8080"),
            new("Seguimiento", configuration["Services:Seguimiento:BaseUrl"] ?? "http://seguimiento:8080"),
            new("Asistente", configuration["Services:Asistente:BaseUrl"] ?? "http://asistente:8080")
        };
    }
}

