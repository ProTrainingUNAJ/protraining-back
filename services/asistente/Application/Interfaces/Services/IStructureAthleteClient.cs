namespace Application.Interfaces.Services;

public sealed record StructureAthleteSummaryDto(
    Guid Id,
    string FullName,
    Guid? CategoryId = null,
    bool Active = true,
    Guid PfId = default,
    IReadOnlyList<Guid>? CategoryIds = null)
{
    /// <summary>
    /// Devuelve todas las categorías activas asignadas al atleta, consolidando 'CategoryIds' o 'CategoryId' para compatibilidad (#182 · KAN-477).
    /// </summary>
    public IReadOnlyList<Guid> AllCategoryIds
    {
        get
        {
            if (CategoryIds is { Count: > 0 })
            {
                return CategoryIds.Where(id => id != Guid.Empty).Distinct().ToList();
            }

            return CategoryId.HasValue && CategoryId.Value != Guid.Empty
                ? [CategoryId.Value]
                : [];
        }
    }
}

/// <summary>
/// Estado del permiso del tutor para recomendar hábitos y alimentación a un atleta (#201 · KAN-496).
/// </summary>
public sealed record StructureAthleteHabitsPermissionDto(
    bool IsMinor,
    bool AllowsHabitsRecommendations);

public sealed record StructureCategoryDto(
    Guid Id,
    string Name);

public sealed record StructureCategoryAthleteDto(
    Guid Id,
    bool Active);

/// <summary>
/// Perfil físico canónico del atleta en Estructura. Se expone como texto ya etiquetado
/// en español para que las reglas de hábitos lo consuman sin duplicar traducciones.
/// </summary>
public sealed record StructureAthleteProfileDto(
    Guid Id,
    double? WeightKg,
    string? AthleticGoal);

/// <summary>
/// Disponibilidad de la ficha médica para el flujo de recomendaciones.
/// </summary>
public enum StructureMedicalRecordAvailability
{
    Available,
    NoRecord,
    Unavailable
}

/// <summary>
/// Datos mínimos de la ficha médica necesarios para evaluar seguridad nutricional.
/// El texto clínico viaja solo en memoria entre Estructura y el handler; nunca se persiste
/// en el snapshot ni se envía al proveedor de IA.
/// </summary>
/// <remarks>
/// <see cref="HasKnownAllergies"/> y <see cref="HasKnownConditions"/> son un seam forward-compatible:
/// Estructura hoy no expone esos campos estructurados (el texto libre vive en pathologies/history/surgeries),
/// así que llegan null y se usa la clasificación conservadora por marcadores. Cuando el campo exista,
/// el cliente lo deserializa y las reglas lo prefieren sin cambiar el flujo. Un valor false NUNCA
/// habilita cantidades si el texto clínico sigue registrando un riesgo: gana la opción más conservadora.
/// </remarks>
public sealed record StructureMedicalRecordContextDto(
    StructureMedicalRecordAvailability Availability,
    bool HasPhysicalFitnessCertificate,
    DateOnly? PhysicalFitnessExpiresAt,
    string? Pathologies,
    string? MedicalHistory,
    string? Surgeries,
    bool? HasKnownAllergies = null,
    bool? HasKnownConditions = null);

public interface IStructureAthleteClient
{
    /// <summary>
    /// Verifica si un atleta existe y pertenece al preparador físico autenticado (KAN-423 / KAN-431).
    /// </summary>
    Task<bool> IsOwnAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene si el atleta es menor de edad y si su tutor autorizó las recomendaciones de hábitos (#201 · KAN-496).
    /// Devuelve <c>null</c> cuando Estructura no responde (404, 5xx, timeout) o el atleta no existe:
    /// el consumidor debe asumir que no hay permiso y no generar recomendación (fail-closed).
    /// </summary>
    Task<StructureAthleteHabitsPermissionDto?> GetHabitsPermissionAsync(Guid athleteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene si el atleta autenticado es menor y si su tutor autorizó las recomendaciones,
    /// consultando Estructura GET /api/v1/me/consent con el token del atleta (#201 · KAN-493).
    /// Devuelve <c>null</c> si no se puede verificar: el consumidor debe bloquear (fail-closed).
    /// </summary>
    Task<StructureAthleteHabitsPermissionDto?> GetMyHabitsPermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene la lista de atletas activos para el análisis del worker nocturno (#177 · KAN-472).
    /// </summary>
    Task<IReadOnlyList<StructureAthleteSummaryDto>> GetAthletesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene las categorías asignadas a un atleta del preparador físico (#196 · KAN-491).
    /// </summary>
    Task<IReadOnlyList<StructureCategoryDto>> GetAthleteCategoriesAsync(Guid athleteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene los datos básicos de una categoría del preparador físico, o null si no existe / no le pertenece (#196 · KAN-491).
    /// </summary>
    Task<StructureCategoryDto?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene los identificadores de los atletas activos de una categoría sin datos personales (#196 · KAN-491).
    /// </summary>
    Task<IReadOnlyList<StructureCategoryAthleteDto>> GetCategoryAthletesAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve el identificador de atleta del usuario autenticado (rol Atleta) consultando GET /api/v1/me en Estructura (#197 · KAN-492).
    /// Devuelve null si el usuario todavía no tiene un perfil de atleta vinculado (404).
    /// </summary>
    Task<Guid?> GetMyAthleteIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee la ficha médica del atleta mediante el endpoint auditado de Estructura.
    /// Requiere token del preparador físico y consentimiento firmado; ante cualquier error
    /// devuelve <see cref="StructureMedicalRecordAvailability.Unavailable"/> para que el
    /// consumidor aplique derivación profesional (fail-closed). Nunca devuelve null como
    /// equivalente a "sin alergias".
    /// </summary>
    Task<StructureMedicalRecordContextDto> GetMedicalRecordContextAsync(
        Guid athleteId,
        string justification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee el perfil físico canónico (peso/objetivo) de Estructura con el token del PF.
    /// Devuelve null si el atleta no existe, no pertenece al PF o el servicio no responde:
    /// el proveedor de hábitos debe responder sin cantidades, nunca inventar valores.
    /// </summary>
    Task<StructureAthleteProfileDto?> GetAthleteProfileAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);
}
