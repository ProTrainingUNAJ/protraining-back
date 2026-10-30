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

        var weeks = request.Weeks is >= 1 and <= 8 ? request.Weeks : 4;
        var daysPerWeek = request.DaysPerWeek is >= 1 and <= 6 ? request.DaysPerWeek : 3;

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
            }
        }

        var (contentDto, provider) = await aiService.GeneratePlanDraftAsync(normalizedRequest, matchDates, cancellationToken);

        var contentJson = PlanDraftMapper.SerializeContent(contentDto);

        var draft = PlanDraft.Create(
            categoryId: hasCategory ? request.CategoryId : null,
            athleteId: hasAthlete ? request.AthleteId : null,
            goal: trimmedGoal,
            startDate: startDate,
            content: contentJson,
            provider: provider
        );

        await planDraftRepository.AddAsync(draft, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger?.LogInformation(
            "Borrador de planificación {DraftId} creado exitosamente con proveedor {Provider} para {RecipientType} {RecipientId}",
            draft.Id,
            provider,
            hasAthlete ? "Atleta" : "Categoría",
            hasAthlete ? request.AthleteId : request.CategoryId);

        return PlanDraftMapper.ToResponseDto(draft, contentDto);
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
