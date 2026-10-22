using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Persists AI execution audit logs and metrics in the database.
/// Ensures failures in logging do not fail the overall application flow.
/// </summary>
public sealed class AiAuditService(
    AppDbContext dbContext,
    ICurrentUser currentUser,
    ILogger<AiAuditService> logger) : IAiAuditService
{
    public async Task LogAsync(AiLog log, CancellationToken cancellationToken = default)
    {
        try
        {
            if (currentUser.PfId.HasValue)
            {
                log.AssignPfId(currentUser.PfId.Value);
            }

            await dbContext.AiLogs.AddAsync(log, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Registro de IA guardado en 'ai_logs' [Proveedor: {Provider}, Latencia: {LatencyMs} ms, Válido: {IsValid}, Tokens: {TotalTokens}].",
                log.Provider,
                log.LatencyMs,
                log.IsValid,
                log.TotalTokens ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo persistir el registro de auditoría en la tabla 'ai_logs': {Message}", ex.Message);
        }
    }
}
