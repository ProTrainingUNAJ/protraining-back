namespace Application.Interfaces.Services;

using Application.Common.Ai;
using Application.DTOs.Ai;

/// <summary>
/// Validador semántico en C# encargado de verificar la existencia real de los ejercicios y sus IDs,
/// corregir identificadores inexistentes contra el catálogo del club y conmutar a generación determinística
/// segura en caso de propuestas no reconciliables con el catálogo.
/// </summary>
public interface IExerciseSemanticValidator
{
    /// <summary>
    /// Inspecciona una propuesta de rutina, valida la existencia de cada ejercicioId y ejercicioNombre
    /// en el catálogo, corrige IDs inválidos y marca ejercicios de referencia según corresponda.
    /// </summary>
    Task<RoutineSemanticValidationResult> ValidateDraftAsync(
        RoutineSuggestionResponseDto draft,
        SemanticValidationPolicy? policy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta la validación semántica completa y, en caso de que la propuesta requiera conmutación
    /// o no sea sanitizable, genera una propuesta determinística segura garantizando ejercicios del catálogo.
    /// </summary>
    Task<RoutineSuggestionResponseDto> ValidateOrFallbackAsync(
        RoutineSuggestionResponseDto draft,
        RoutineSuggestionRequestDto originalRequest,
        SemanticValidationPolicy? policy = null,
        CancellationToken cancellationToken = default);
}
