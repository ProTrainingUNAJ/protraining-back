using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Persiste registros de auditoría y métricas de IA en la tabla ai_logs.
/// Crea un AppDbContext independiente por escritura vía IDbContextFactory para garantizar
/// seguridad ante concurrencia y no interferir con el ChangeTracker del pedido (KAN-491).
/// </summary>
public sealed class AiAuditService(
    IDbContextFactory<AppDbContext> dbContextFactory,
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

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
