namespace Application.UseCases.PlanDrafts.CreatePlanDraft;

using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;

public sealed class CreatePlanDraftCommandHandler(
    IAiService aiService,
    IPlanDraftRepository planDraftRepository,
    IUnitOfWork unitOfWork,
    IStructureCalendarClient? calendarClient = null,
    IMatchDayLoadValidator? matchDayLoadValidator = null,
    IExerciseCatalogService? catalogService = null,
    ILogger<CreatePlanDraftCommandHandler>? logger = null)
{
    public async Task<PlanDraftResponseDto> HandleAsync(
        CreatePlanDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;

        if (string.IsNullOrWhiteSpace(request.Goal))
            throw new DomainException("El objetivo de entrenamiento es obligatorio.");

        var trimmedGoal = request.Goal.Trim();
        if (trimmedGoal.Length > PlanDraft.MaxGoalLength)
            throw new DomainException($"El objetivo no puede superar los {PlanDraft.MaxGoalLength} caracteres.");

        var hasCategory = request.CategoryId.HasValue && request.CategoryId.Value != Guid.Empty;
        var hasAthlete = request.AthleteId.HasValue && request.AthleteId.Value != Guid.Empty;

        if (hasCategory == hasAthlete)
            throw new DomainException("Debés indicar una categoría o un atleta, pero no ambos a la vez.");

        // KAN-563: Validación estricta de rangos semanas (1 a 8) y días por semana (1 a 6) retornando 400
        if (request.Weeks is < 1 or > 8)
            throw new DomainException("La cantidad de semanas debe estar entre 1 y 8.");

        if (request.DaysPerWeek is < 1 or > 6)
            throw new DomainException("La cantidad de días por semana debe estar entre 1 y 6.");

        var weeks = request.Weeks;
        var daysPerWeek = request.DaysPerWeek;

        var startDate = request.StartDate.HasValue && request.StartDate.Value != default
            ? request.StartDate.Value
            : CalculateNextMondayInArgentina();

        var normalizedRequest = request with
        {
            Goal = trimmedGoal,
            Weeks = weeks,
            DaysPerWeek = daysPerWeek,
            StartDate = startDate
        };

        IReadOnlyList<DateOnly>? matchDates = null;
        var calendarReadFailed = false;
        if (hasCategory && calendarClient != null)
        {
            try
            {
                var endDate = startDate.AddDays((weeks * 7) - 1);
                matchDates = await calendarClient.GetMatchDatesAsync(
                    request.CategoryId!.Value,
                    startDate,
                    endDate,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "No se pudieron obtener las fechas de partidos para la categoría {CategoryId}. Se continuará la generación sin restricciones de calendario.",
                    request.CategoryId);
                matchDates = null;
                calendarReadFailed = true;
            }
        }

        var (contentDto, provider) = await aiService.GeneratePlanDraftAsync(normalizedRequest, matchDates, cancellationToken);
        logger?.LogInformation("Borrador de planificación creado exitosamente con proveedor '{Provider}'.", provider);

        var finalContent = contentDto;
        IReadOnlyList<string> corrections = [];

        // KAN-408 / KAN-563: Validador algorítmico post-IA para corregir cargas pre-partido (D-1 <= 60%, D-2 <= 75%, D sin pesas)
        // Se buscan los nombres exactos de los ejercicios presentes en el borrador mediante FindByIdAsync
        if (matchDayLoadValidator != null && matchDates is { Count: > 0 })
        {
            IReadOnlyDictionary<Guid, string>? exerciseNames = null;
            if (catalogService != null)
            {
                try
                {
                    var distinctExerciseIds = (contentDto.Weeks ?? [])
                        .SelectMany(w => w.Sessions ?? [])
                        .SelectMany(s => s.Exercises ?? [])
                        .Select(e => e.ExerciseId)
                        .Where(id => id != Guid.Empty)
                        .Distinct()
                        .ToList();

                    var dict = new Dictionary<Guid, string>();
                    foreach (var id in distinctExerciseIds)
                    {
                        var ex = await catalogService.FindByIdAsync(id, cancellationToken);
                        if (ex != null)
                        {
                            dict[ex.Id] = ex.Name;
                        }
                    }

                    // Fallback para mocks o catálogos que solo configuran GetAllAsync
                    if (dict.Count < distinctExerciseIds.Count)
                    {
                        var all = await catalogService.GetAllAsync(cancellationToken);
                        if (all is { Count: > 0 })
                        {
                            foreach (var ex in all)
                            {
                                dict.TryAdd(ex.Id, ex.Name);
                            }
                        }
                    }

                    if (dict.Count > 0)
                    {
                        exerciseNames = dict;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "No se pudo consultar el catálogo para obtener los nombres de ejercicios en el validador de partidos.");
                }
            }

            var validationResult = matchDayLoadValidator.ValidateAndAdjust(
                contentDto,
                startDate,
                matchDates,
                exerciseNames);

            finalContent = validationResult.Content;
            corrections = validationResult.Corrections;
            logger?.LogInformation("El validador de cargas pre-partido aplicó {Count} corrección(es).", corrections.Count);
        }

        var warnings = new List<string>();
        if (calendarReadFailed)
        {
            warnings.Add("No se pudo leer el calendario de partidos. El borrador se generó sin restricciones de calendario pre-partido.");
        }
        if (corrections is { Count: > 0 })
        {
            warnings.AddRange(corrections);
        }

        var serializedContent = PlanDraftMapper.SerializeContent(finalContent);

        var draft = PlanDraft.Create(
            categoryId: hasCategory ? request.CategoryId : null,
            athleteId: hasAthlete ? request.AthleteId : null,
            goal: trimmedGoal,
            startDate: startDate,
            content: serializedContent,
            provider: provider
        );

        await planDraftRepository.AddAsync(draft, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PlanDraftMapper.ToResponseDto(draft, finalContent, warnings);
    }

    public static DateOnly CalculateNextMondayInArgentina(DateTime? utcNow = null)
    {
        var utc = utcNow ?? DateTime.UtcNow;
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Argentina Standard Time" : "America/Argentina/Buenos_Aires");
        }
        catch
        {
            tz = TimeZoneInfo.CreateCustomTimeZone("ART", TimeSpan.FromHours(-3), "ART", "ART");
        }

        var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        var date = DateOnly.FromDateTime(localDateTime);

        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0)
            daysUntilMonday = 7; // Próximo lunes

        return date.AddDays(daysUntilMonday);
    }
}
