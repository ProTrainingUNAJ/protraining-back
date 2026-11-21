using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

/// <summary>
/// Cliente HTTP para validar la pertenencia y existencia de atletas en Estructura (KAN-423 / KAN-431 / KAN-472 / KAN-491 / KAN-496).
/// El aislamiento multi-tenant por PfId se propaga con el token del PF o token de Servicio a través de ForwardAuthorizationHandler.
/// </summary>
public sealed class StructureAthleteClient(
    HttpClient httpClient,
    ILogger<StructureAthleteClient> logger) : IStructureAthleteClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record StructureAthleteApiResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("pfId")] Guid PfId,
        [property: JsonPropertyName("fullName")] string? FullName,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("active")] bool Active,
        [property: JsonPropertyName("categoryId")] Guid? CategoryId,
        [property: JsonPropertyName("categoryIds")] IReadOnlyList<Guid>? CategoryIds = null,
        [property: JsonPropertyName("weightKg")] double? WeightKg = null,
        [property: JsonPropertyName("athleticGoal")] string? AthleticGoal = null);

    public async Task<bool> IsOwnAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            return false;

        if (httpClient.BaseAddress is null)
            throw new StructureServiceUnavailableException("La dirección base del microservicio de Estructura no está configurada.");

        try
        {
            var url = $"api/v1/athletes/{athleteId}";
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
                return true;

            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;

            logger.LogWarning(
                "Estructura respondió código HTTP {StatusCode} al verificar el atleta {AthleteId}.",
                (int)response.StatusCode,
                athleteId);

            throw new StructureServiceUnavailableException(
                $"El microservicio de Estructura no se encuentra disponible (código {(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructureServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Error al comunicarse con Estructura para verificar el atleta {AthleteId}: {Message}",
                athleteId,
                ex.Message);

            throw new StructureServiceUnavailableException(
                "El microservicio de Estructura no se encuentra disponible.", ex);
        }
    }

    // Campos anulables para tolerar JSON incompleto (KAN-496): si alguno viene null o ausente,
    // no se puede verificar edad ni permiso del tutor y se resuelve en modo fail-closed.
    private sealed record StructureAthletePermissionApiResponse(
        [property: JsonPropertyName("isMinor")] bool? IsMinor,
        [property: JsonPropertyName("allowsHabitsRecommendations")] bool? AllowsHabitsRecommendations);

    public Task<StructureAthleteHabitsPermissionDto?> GetHabitsPermissionAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            return Task.FromResult<StructureAthleteHabitsPermissionDto?>(null);

        return GetHabitsPermissionCoreAsync(
            $"api/v1/athletes/{athleteId}",
            $"del atleta {athleteId}",
            cancellationToken);
    }

    /// <summary>
    /// El token de un atleta no tiene acceso a GET /api/v1/athletes/{id}: Estructura expone
    /// su propio estado de consentimiento en GET /api/v1/me/consent (KAN-493).
    /// </summary>
    public Task<StructureAthleteHabitsPermissionDto?> GetMyHabitsPermissionAsync(CancellationToken cancellationToken = default) =>
        GetHabitsPermissionCoreAsync("api/v1/me/consent", "del atleta autenticado", cancellationToken);

    private async Task<StructureAthleteHabitsPermissionDto?> GetHabitsPermissionCoreAsync(
        string url,
        string athleteDescription,
        CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            logger.LogError("La dirección base de Estructura no está configurada.");
            return null;
        }

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Estructura respondió código HTTP {StatusCode} al consultar el permiso de recomendaciones {AthleteDescription}. No se generará recomendación (fail-closed).",
                    (int)response.StatusCode,
                    athleteDescription);
                return null;
            }

            var athlete = await response.Content.ReadFromJsonAsync<StructureAthletePermissionApiResponse>(cancellationToken: cancellationToken);
            if (athlete is null)
            {
                logger.LogWarning(
                    "Estructura devolvió una respuesta vacía al consultar el permiso de recomendaciones {AthleteDescription}. No se generará recomendación (fail-closed).",
                    athleteDescription);
                return null;
            }

            // Fail-closed (KAN-496): sin 'isMinor' o 'allowsHabitsRecommendations' explícitos no se
            // puede saber la edad ni el permiso del tutor; nunca se asume que un menor es adulto con permiso.
            if (athlete.IsMinor is null || athlete.AllowsHabitsRecommendations is null)
            {
                logger.LogWarning(
                    "Estructura no informó 'isMinor' o 'allowsHabitsRecommendations' (null o ausente) al consultar el permiso de recomendaciones {AthleteDescription}. No se generará recomendación de alimentación (fail-closed, KAN-496).",
                    athleteDescription);
                return null;
            }

            return new StructureAthleteHabitsPermissionDto(athlete.IsMinor.Value, athlete.AllowsHabitsRecommendations.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error al comunicarse con Estructura para consultar el permiso de recomendaciones {AthleteDescription}. No se generará recomendación (fail-closed).",
                athleteDescription);
            return null;
        }
    }

    public async Task<IReadOnlyList<StructureAthleteSummaryDto>> GetAthletesAsync(CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null)
        {
            logger.LogError("La dirección base de Estructura no está configurada.");
            throw new StructureServiceUnavailableException("La dirección base del microservicio de Estructura no está configurada.");
        }

        try
        {
            const string url = "api/v1/internal/athletes";
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Estructura respondió código HTTP {StatusCode} al obtener atletas para el worker nocturno.",
                    (int)response.StatusCode);

                throw new StructureServiceUnavailableException(
                    $"El microservicio de Estructura no se encuentra disponible (código {(int)response.StatusCode}).");
            }

            var apiAthletes = await response.Content.ReadFromJsonAsync<List<StructureAthleteApiResponse>>(cancellationToken: cancellationToken);
            if (apiAthletes is null || apiAthletes.Count == 0)
            {
                return [];
            }

            return apiAthletes
                .Where(a => a.Active)
                .Select(a =>
                {
                    var name = !string.IsNullOrWhiteSpace(a.FullName)
                        ? a.FullName.Trim()
                        : $"{a.FirstName} {a.LastName}".Trim();

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "Atleta " + a.Id.ToString()[..8];
                    }

                    return new StructureAthleteSummaryDto(
                        Id: a.Id,
                        FullName: name,
                        CategoryId: a.CategoryId,
                        Active: a.Active,
                        PfId: a.PfId,
                        CategoryIds: a.CategoryIds);
                })
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructureServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al comunicarse con Estructura para obtener atletas.");
            throw new StructureServiceUnavailableException("El microservicio de Estructura no se encuentra disponible.", ex);
        }
    }

    public async Task<IReadOnlyList<StructureCategoryDto>> GetAthleteCategoriesAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        var result = await GetFromStructureAsync<List<StructureCategoryDto>>(
            relativeUrl: $"api/v1/athletes/{athleteId}/categories",
            returnNullOn404: false,
            notFoundMessage: "El atleta no existe o no pertenece al preparador físico autenticado.",
            operationDescription: $"consultar las categorías del atleta {athleteId}",
            cancellationToken: cancellationToken);

        return result ?? [];
    }

    public Task<StructureCategoryDto?> GetCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        if (categoryId == Guid.Empty)
            return Task.FromResult<StructureCategoryDto?>(null);

        return GetFromStructureAsync<StructureCategoryDto>(
            relativeUrl: $"api/v1/categories/{categoryId}",
            returnNullOn404: true,
            notFoundMessage: "La categoría no existe o no pertenece al preparador físico autenticado.",
            operationDescription: $"consultar la categoría {categoryId}",
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<StructureCategoryAthleteDto>> GetCategoryAthletesAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        if (categoryId == Guid.Empty)
            throw new NotFoundException("La categoría no existe o no pertenece al preparador físico autenticado.");

        var athletes = await GetFromStructureAsync<List<StructureCategoryAthleteDto>>(
            relativeUrl: $"api/v1/categories/{categoryId}/athletes",
            returnNullOn404: false,
            notFoundMessage: "La categoría no existe o no pertenece al preparador físico autenticado.",
            operationDescription: $"consultar los atletas de la categoría {categoryId}",
            cancellationToken: cancellationToken);

        return athletes?
            .Where(a => a.Active && a.Id != Guid.Empty)
            .ToList()
            ?? [];
    }

    /// <summary>
    /// Perfil físico canónico desde GET /api/v1/athletes/{id}. Se usa el token del PF
    /// reenviado (ForwardAuthorizationHandler); un atleta ajeno da 404 por el filtro de tenant.
    /// </summary>
    public async Task<StructureAthleteProfileDto?> GetAthleteProfileAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            return null;

        try
        {
            var athlete = await GetFromStructureAsync<StructureAthleteApiResponse>(
                relativeUrl: $"api/v1/athletes/{athleteId}",
                returnNullOn404: true,
                notFoundMessage: "El atleta no existe o no pertenece al preparador físico autenticado.",
                operationDescription: $"consultar el perfil físico del atleta {athleteId}",
                cancellationToken: cancellationToken);

            return athlete is null
                ? null
                : new StructureAthleteProfileDto(athlete.Id, athlete.WeightKg, athlete.AthleticGoal);
        }
        catch (StructureServiceUnavailableException ex)
        {
            logger.LogWarning(
                ex,
                "No se pudo consultar el perfil físico del atleta {AthleteId}; la recomendación se genera sin cantidades.",
                athleteId);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(
                ex,
                "Estructura rechazó la consulta del perfil físico del atleta {AthleteId}; se genera sin cantidades.",
                athleteId);
            return null;
        }
    }

    private sealed record StructureMeApiResponse(
        [property: JsonPropertyName("athleteId")] Guid AthleteId);

    private sealed record StructureMedicalRecordApiResponse(
        [property: JsonPropertyName("id")] Guid? Id,
        [property: JsonPropertyName("hasPhysicalFitnessCertificate")] bool HasPhysicalFitnessCertificate,
        [property: JsonPropertyName("physicalFitnessExpiresAt")] DateOnly? PhysicalFitnessExpiresAt,
        [property: JsonPropertyName("pathologies")] string? Pathologies,
        [property: JsonPropertyName("medicalHistory")] string? MedicalHistory,
        [property: JsonPropertyName("surgeries")] string? Surgeries,
        // Campos aún no expuestos por Estructura. Si aparecen, se consumen sin tocar el flujo.
        [property: JsonPropertyName("hasKnownAllergies")] bool? HasKnownAllergies = null,
        [property: JsonPropertyName("hasKnownConditions")] bool? HasKnownConditions = null);

    /// <summary>
    /// La ficha médica se lee por el endpoint auditado de Estructura con el token
    /// del PF (este HttpClient usa ForwardAuthorizationHandler). El token de Servicio no
    /// tiene acceso a este endpoint, por lo que la lectura solo ocurre en el request del PF.
    /// Cualquier fallo se reporta como No disponible; nunca como "sin alergias".
    /// </summary>
    public async Task<StructureMedicalRecordContextDto> GetMedicalRecordContextAsync(
        Guid athleteId,
        string justification,
        CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty || string.IsNullOrWhiteSpace(justification))
            return UnavailableMedicalRecord();

        if (httpClient.BaseAddress is null)
        {
            logger.LogError("La dirección base de Estructura no está configurada; no se puede consultar la ficha médica.");
            return UnavailableMedicalRecord();
        }

        try
        {
            var url = $"api/v1/athletes/{athleteId}/medical-record/access";
            using var response = await httpClient.PostAsJsonAsync(
                url,
                new { justification },
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "No se pudo verificar la ficha médica del atleta {AthleteId} (HTTP {StatusCode}). Se aplicará derivación profesional (fail-closed).",
                    athleteId,
                    (int)response.StatusCode);
                return UnavailableMedicalRecord();
            }

            var record = await response.Content.ReadFromJsonAsync<StructureMedicalRecordApiResponse>(
                JsonOptions,
                cancellationToken);

            if (record is null)
            {
                logger.LogWarning("Estructura devolvió una ficha médica vacía para el atleta {AthleteId}.", athleteId);
                return UnavailableMedicalRecord();
            }

            var availability = record.Id.HasValue && record.Id.Value != Guid.Empty
                ? StructureMedicalRecordAvailability.Available
                : StructureMedicalRecordAvailability.NoRecord;

            return new StructureMedicalRecordContextDto(
                availability,
                record.HasPhysicalFitnessCertificate,
                record.PhysicalFitnessExpiresAt,
                record.Pathologies,
                record.MedicalHistory,
                record.Surgeries,
                record.HasKnownAllergies,
                record.HasKnownConditions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error al consultar la ficha médica del atleta {AthleteId}. Se aplicará derivación profesional (fail-closed).",
                athleteId);
            return UnavailableMedicalRecord();
        }
    }

    private static StructureMedicalRecordContextDto UnavailableMedicalRecord() =>
        new(StructureMedicalRecordAvailability.Unavailable, false, null, null, null, null);

    public async Task<Guid?> GetMyAthleteIdAsync(CancellationToken cancellationToken = default)
    {
        var me = await GetFromStructureAsync<StructureMeApiResponse>(
            relativeUrl: "api/v1/me",
            returnNullOn404: true,
            notFoundMessage: "No se encontró un perfil de atleta vinculado al usuario autenticado.",
            operationDescription: "resolver el perfil del atleta autenticado",
            cancellationToken: cancellationToken);

        return me is not null && me.AthleteId != Guid.Empty ? me.AthleteId : null;
    }

    private async Task<T?> GetFromStructureAsync<T>(
        string relativeUrl,
        bool returnNullOn404,
        string notFoundMessage,
        string operationDescription,
        CancellationToken cancellationToken) where T : class
    {
        if (httpClient.BaseAddress is null)
        {
            logger.LogError("La dirección base de Estructura no está configurada.");
            throw new StructureServiceUnavailableException("La dirección base del microservicio de Estructura no está configurada.");
        }

        try
        {
            using var response = await httpClient.GetAsync(relativeUrl, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (returnNullOn404)
                    return null;

                throw new NotFoundException(notFoundMessage);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException(
                    $"Estructura rechazó la autenticación o permisos al {operationDescription}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Estructura respondió código HTTP {StatusCode} al {Operation}.",
                    (int)response.StatusCode,
                    operationDescription);
                throw new StructureServiceUnavailableException(
                    $"El microservicio de Estructura no se encuentra disponible (código {(int)response.StatusCode}).");
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (StructureServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al comunicarse con Estructura al {Operation}.", operationDescription);
            throw new StructureServiceUnavailableException("El microservicio de Estructura no se encuentra disponible.", ex);
        }
    }
}
