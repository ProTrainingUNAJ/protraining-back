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

    public async Task<bool> DispatchPurgeToExternalServicesAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        var servicesToPurge = GetTargetServices();
        var allSucceeded = true;

        foreach (var target in servicesToPurge)
        {
            var success = false;
            const int maxAttempts = 2; // Reintento inmediato ante fallo transitorio

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

                logger.LogWarning(
                    "Intento {Attempt}/{MaxAttempts} fallido al purgar atleta {AthleteId} en {ServiceName}. Motivo: {Error}",
                    attempt,
                    maxAttempts,
                    athleteId,
                    target.Name,
                    result.ErrorMessage);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(150, cancellationToken);
                }
            }

            if (!success)
            {
                allSucceeded = false;
                logger.LogWarning(
                    "El servicio {ServiceName} no pudo completar la purga del atleta {AthleteId} tras {MaxAttempts} intentos. Se aborta el ciclo para este atleta y se reintentará más tarde.",
                    target.Name,
                    athleteId,
                    maxAttempts);
                break; // No seguimos con los restantes si uno falló, para garantizar consistencia y reintentar todo el bloque
            }
        }

        return allSucceeded;
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

