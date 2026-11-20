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
    private sealed record ServiceTarget(string Name, string BaseUrl, Guid? TargetId);

    public async Task<AthletePurgeDispatchResult> DispatchPurgeToExternalServicesAsync(
        Guid athleteId,
        Guid? accountId = null,
        CancellationToken cancellationToken = default)
    {
        var servicesToPurge = GetTargetServices(athleteId, accountId);
        var allSucceeded = true;
        string? failureReason = null;

        foreach (var target in servicesToPurge)
        {
            // Si el servicio requiere un identificador específico (ej: AccountId para Identidad)
            // y el atleta no posee cuenta creada, se omite limpiamente la llamada (#204 · KAN-499).
            if (!target.TargetId.HasValue)
            {
                logger.LogInformation(
                    "El atleta {AthleteId} no posee cuenta de usuario vinculada en {ServiceName} (identificador nulo). Se omite llamada.",
                    athleteId,
                    target.Name);
                continue;
            }

            var targetId = target.TargetId.Value;
            var success = false;
            const int maxAttempts = 3; // Reintentos con retroceso exponencial ante fallos transitorios de red
            string? lastServiceError = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var result = await purgeClient.PurgeAthleteInServiceAsync(
                    target.Name,
                    target.BaseUrl,
                    targetId,
                    cancellationToken);

                if (result is not null && result.IsSuccess)
                {
                    success = true;
                    break;
                }

                lastServiceError = result?.ErrorMessage ?? "Respuesta nula del cliente de purga";
                logger.LogWarning(
                    "Intento {Attempt}/{MaxAttempts} fallido al purgar atleta {AthleteId} en {ServiceName}. Motivo: {Error}",
                    attempt,
                    maxAttempts,
                    athleteId,
                    target.Name,
                    lastServiceError);

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

    private List<ServiceTarget> GetTargetServices(Guid athleteId, Guid? accountId)
    {
        return
        [
            new("Identidad", configuration["Services:Identidad:BaseUrl"] ?? "http://identidad:8080", accountId),
            new("Entrenamiento", configuration["Services:Entrenamiento:BaseUrl"] ?? "http://entrenamiento:8080", athleteId),
            new("Seguimiento", configuration["Services:Seguimiento:BaseUrl"] ?? "http://seguimiento:8080", athleteId),
            new("Asistente", configuration["Services:Asistente:BaseUrl"] ?? "http://asistente:8080", athleteId)
        ];
    }
}

