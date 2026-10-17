namespace Application.Interfaces.Services;

using Application.DTOs.Ai;

/// <summary>
/// Contrato del servicio de caché en memoria para respuestas de IA (KAN-368).
/// Almacena y recupera propuestas estructuradas calculando el hash de las instrucciones
/// para evitar recomputaciones innecesarias ante solicitudes idénticas.
/// </summary>
public interface IAiResponseCache
{
    /// <summary>
    /// Intenta obtener una propuesta de rutina previamente calculada a partir del hash de sus instrucciones.
    /// </summary>
    bool TryGet(string promptHash, out RoutineSuggestionResponseDto? cachedResponse);

    /// <summary>
    /// Guarda en caché la propuesta de rutina asociándola al hash de sus instrucciones con vencimiento temporal.
    /// </summary>
    void Set(string promptHash, RoutineSuggestionResponseDto response, TimeSpan? customTtl = null);

    /// <summary>
    /// Calcula un hash determinístico SHA-256 a partir del objetivo, los grupos musculares solicitados y opcionalmente el identificador del preparador físico (KAN-368).
    /// </summary>
    string ComputePromptHash(string goal, IEnumerable<string>? muscleGroupEmphasis, Guid? pfId = null);

    /// <summary>
    /// Calcula un hash determinístico SHA-256 a partir de un texto de prompt crudo.
    /// </summary>
    string ComputePromptHash(string promptText);
}
