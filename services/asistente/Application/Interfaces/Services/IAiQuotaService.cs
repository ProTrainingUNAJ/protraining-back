namespace Application.Interfaces.Services;

/// <summary>
/// Gestiona la cuota y el límite diario en memoria de consultas a modelos de IA por Preparador Físico (#215 · KAN-510).
/// Previene el agotamiento prematuro de saldo de proveedores externos garantizando que ante exceso
/// el sistema derive automáticamente a reglas determinísticas sin interrumpir el flujo.
/// </summary>
public interface IAiQuotaService
{
    /// <summary>
    /// Comprueba si el preparador físico tiene cuota disponible para el día actual.
    /// Si no se especifica un identificador (llamada de sistema/interna), retorna siempre true.
    /// </summary>
    bool HasQuota(Guid? pfId);

    /// <summary>
    /// Registra el consumo de una solicitud de IA para el preparador físico en el día de la fecha.
    /// Retorna la cantidad de solicitudes consumidas en el día actual.
    /// </summary>
    int RecordUsage(Guid? pfId);

    /// <summary>
    /// Retorna la cantidad de solicitudes consumidas por el preparador físico en el día actual.
    /// </summary>
    int GetCurrentUsage(Guid? pfId);

    /// <summary>
    /// Límite diario de solicitudes configurado por preparador físico.
    /// </summary>
    int DailyLimit { get; }
}
