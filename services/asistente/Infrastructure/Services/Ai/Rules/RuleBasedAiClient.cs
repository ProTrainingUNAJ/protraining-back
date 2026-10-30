using System.Diagnostics;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai.Rules;

public sealed class RuleBasedAiClient(
    IAiAuditService aiAuditService,
    ILogger<RuleBasedAiClient> logger,
    IExerciseCatalogService? catalogService = null) : IAiClient
{
    public string ProviderName => "Reglas";

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        logger?.LogInformation("Generando propuesta de entrenamiento mediante motor de reglas determinísticas para el objetivo: '{Goal}'.", request.Goal);

        var (title, justification, exercises) = DeterministicRuleEngine.GenerateProposal(
            request.Goal,
            request.MuscleGroupEmphasis);

        stopwatch.Stop();

        var prompt = $"Objetivo: {request.Goal}. Énfasis muscular: {(request.MuscleGroupEmphasis is { Count: > 0 } ? string.Join(", ", request.MuscleGroupEmphasis) : "General")}";

        var response = new RoutineSuggestionResponseDto(
            AthleteId: request.AthleteId,
            ProposedTitle: title,
            JustificationSummary: justification,
            Exercises: exercises,
            ProviderUsed: ProviderName
        );

        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            stopwatch.ElapsedMilliseconds,
            isValid: true,
            validationResult: "Valido",
            response: title,
            athleteId: request.AthleteId,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0));

        return response;
    }

    public async Task<PlanDraftContentDto?> TryGeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        logger?.LogInformation("Generando borrador de planificación mediante motor de reglas determinísticas para '{Goal}' ({Weeks} semanas, {Days} días/sem).",
            request.Goal, request.Weeks, request.DaysPerWeek);

        IReadOnlyList<CatalogExerciseDto> catalog = [];
        if (catalogService != null)
        {
            catalog = await catalogService.GetAllAsync(cancellationToken);
        }

        var availableExercises = catalog.Count > 0
            ? catalog.ToList()
            : [
                new CatalogExerciseDto(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Sentadilla trasera con barra", "Piernas", "Barra"),
                new CatalogExerciseDto(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Press de banca plano", "Pecho", "Barra"),
                new CatalogExerciseDto(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Peso muerto convencional", "Espalda", "Barra")
            ];

        var weeks = new List<PlanDraftWeekDto>();
        var exerciseIndex = 0;

        DayOfWeek[] defaultDays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday];
        var selectedDays = defaultDays.Take(Math.Min(request.DaysPerWeek, defaultDays.Length))
            .OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d)
            .ToList();

        var matchSet = matchDates != null && matchDates.Count > 0
            ? new HashSet<DateOnly>(matchDates)
            : null;

        var startDate = request.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        for (var w = 1; w <= request.Weeks; w++)
        {
            var weekStart = startDate.AddDays((w - 1) * 7);
            var sessions = new List<PlanDraftSessionDto>();

            foreach (var dayOfWeek in selectedDays)
            {
                var sessionDate = weekStart;
                for (var i = 0; i < 7; i++)
                {
                    var candidate = weekStart.AddDays(i);
                    if (candidate.DayOfWeek == dayOfWeek)
                    {
                        sessionDate = candidate;
                        break;
                    }
                }

                if (matchSet != null && matchSet.Contains(sessionDate) && !MatchDayLoadRules.AllowStrengthSessionOnMatchDay)
                {
                    // Día de partido (D): no se programa sesión de fuerza
                    continue;
                }

                var targetPercentage = 80m;
                var notes = "Progresión base 3x8 al 80%. Enfoque en técnica y control excéntrico.";

                if (matchSet != null && matchSet.Contains(sessionDate.AddDays(1)))
                {
                    targetPercentage = MatchDayLoadRules.MaxPercentageOneDayBeforeMatch;
                    notes = $"Tapering pre-partido (D-1): Carga reducida al {targetPercentage:0.#}% para optimizar recuperación.";
                }
                else if (matchSet != null && matchSet.Contains(sessionDate.AddDays(2)))
                {
                    targetPercentage = MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch;
                    notes = $"Tapering pre-partido (D-2): Carga moderada al {targetPercentage:0.#}% para preservar frescura neuromuscular.";
                }

                var sessionExercises = new List<PlanDraftExerciseDto>();
                for (var e = 1; e <= 2; e++)
                {
                    var catEx = availableExercises[exerciseIndex % availableExercises.Count];
                    exerciseIndex++;

                    sessionExercises.Add(new PlanDraftExerciseDto(
                        ExerciseId: catEx.Id,
                        Order: e,
                        Sets: 3,
                        Reps: 8,
                        TargetPercentage: targetPercentage
                    ));
                }

                var sessionOrder = sessions.Count + 1;
                sessions.Add(new PlanDraftSessionDto(
                    Name: $"Sesión {sessionOrder} - Acondicionamiento y Fuerza",
                    DayOfWeek: dayOfWeek,
                    Order: sessionOrder,
                    CoachNotes: notes,
                    Exercises: sessionExercises
                ));
            }

            weeks.Add(new PlanDraftWeekDto(
                WeekNumber: w,
                Sessions: sessions
            ));
        }

        stopwatch.Stop();

        var justification = matchSet != null && matchSet.Count > 0
            ? $"Planificación adaptada con periodización competitiva ({matchSet.Count} partido(s) oficial(es) considerados). Base 3x8 al 80% con tapering pre-partido (D-1 <= 60%, D-2 <= 75%)."
            : "Planificación base generada por motor de reglas determinísticas con rotación de ejercicios oficiales (3x8 al 80%).";

        var content = new PlanDraftContentDto(
            Name: $"Planificación {request.Goal}",
            Objective: request.Goal,
            JustificationSummary: justification,
            Weeks: weeks
        );

        var prompt = $"Objetivo: {request.Goal}. Semanas: {request.Weeks}. Días: {request.DaysPerWeek}. Partidos: {matchSet?.Count ?? 0}";
        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            stopwatch.ElapsedMilliseconds,
            isValid: true,
            validationResult: "Valido",
            response: content.Name,
            athleteId: request.AthleteId,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0));

        return content;
    }
}
