using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Recommendations.GenerateHabitsRecommendation;

public sealed record GenerateHabitsRecommendationCommand(Guid AthleteId, bool IsMinor);

/// <summary>
/// Command que genera y PERSISTE una recomendación de hábitos/nutrición pendiente (KAN-494).
/// La escritura vive acá para que la Query de lectura no llame a AddAsync/SaveChanges. El endpoint GET
/// del contrato sigue generando cuando no hay pendiente, pero delega en este Command; la autorización
/// (pertenencia, preferencia del PF y consentimiento del tutor) la resuelve la Query antes de invocarlo.
/// </summary>
public sealed class GenerateHabitsRecommendationCommandHandler(
    IStructureAthleteClient structureClient,
    ISeguimientoClient seguimientoClient,
    IStructureCalendarClient calendarClient,
    IAthleteRecommendationRepository recommendationRepository,
    IAiService aiService,
    IUnitOfWork unitOfWork,
    IHabitsAthleteContextProvider athleteContextProvider,
    IHabitsTrainingDayContextProvider trainingDayContextProvider)
{
    public async Task<HabitsRecommendationResponseDto> HandleAsync(
        GenerateHabitsRecommendationCommand command,
        CancellationToken cancellationToken = default)
    {
        var categories = await structureClient.GetAthleteCategoriesAsync(command.AthleteId, cancellationToken);
        var primaryCategory = categories.FirstOrDefault();

        // KAN-494: peso y objetivo salen de la fuente configurada. Si no hay fuente, el proveedor
        // devuelve null y el motor responde sin cantidades: nunca se inventan valores del atleta.
        var athleteContext = await athleteContextProvider.GetAsync(command.AthleteId, cancellationToken);

        // KAN-494: ficha médica real, leída por el endpoint auditado de Estructura con el token
        // del PF. Cualquier fallo (consentimiento, permisos o servicio) se clasifica como no
        // verificable y deriva a profesional; una ficha vacía NUNCA se toma como "sin alergias".
        var medicalRecord = await structureClient.GetMedicalRecordContextAsync(
            command.AthleteId,
            HabitsMedicalContextRules.MedicalRecordJustification,
            cancellationToken);
        var medicalContext = HabitsMedicalContextRules.Evaluate(medicalRecord, ArgentinaTime.Today);

        var today = ArgentinaTime.Today;
        var matchDates = new List<DateOnly>();
        var matchQueriesSucceeded = false;
        foreach (var category in categories)
        {
            try
            {
                var dates = await calendarClient.GetMatchDatesAsync(
                    category.Id,
                    today,
                    today.AddDays(HabitsRecommendationRules.MatchNearbyWindowDaysAssumption),
                    cancellationToken);
                matchDates.AddRange(dates);
                matchQueriesSucceeded = true;
            }
            catch
            {
                // Fail-safe: una categoría sin datos no invalida a las demás; si todas fallan, se desconoce el momento.
            }
        }

        bool? hasNearbyMatch = matchQueriesSucceeded
            ? HabitsRecommendationRules.InferMomentsFromData(matchDates, today, null).hasNearbyMatch
            : null;

        bool? hasInsufficientRest = null;
        var contextNotes = new List<string>();
        try
        {
            var checkIns = await seguimientoClient.GetAthleteCheckInsHistoryAsync(
                [command.AthleteId],
                today.AddDays(-7),
                today,
                cancellationToken);
            var futureDated = checkIns.Count(c => c.Date > today);
            var usableCheckIns = checkIns.Where(c => c.Date <= today).ToList();

            if (futureDated > 0)
                contextNotes.Add("Se descartaron check-ins con fecha futura al inferir el descanso.");

            (_, hasInsufficientRest) = HabitsRecommendationRules.InferMomentsFromData(null, today, usableCheckIns);

            if (usableCheckIns.Count > 0)
            {
                var latestDate = usableCheckIns.Max(c => c.Date);
                if (latestDate < today)
                    contextNotes.Add($"El último check-in disponible es del {latestDate:yyyy-MM-dd}; el descanso puede no ser reciente.");
            }
        }
        catch
        {
            // Fail-safe: sin historial de check-ins, no inferir descanso.
            hasInsufficientRest = null;
        }

        // KAN-494: "día pesado" se toma de la fuente configurada. Sin fuente (hoy) o ante error,
        // queda null y se reporta en missingData: nunca se infiere de carga/1RM/RPE.
        bool? isHeavyTrainingDay;
        try
        {
            isHeavyTrainingDay = await trainingDayContextProvider.IsHeavyTrainingDayAsync(
                command.AthleteId, today, cancellationToken);
        }
        catch
        {
            isHeavyTrainingDay = null;
        }

        var evaluation = HabitsRecommendationRules.Evaluate(new HabitsEvaluationInput(
            AthleticGoal: athleteContext?.AthleticGoal,
            WeightKg: athleteContext?.WeightKg,
            IsMinor: command.IsMinor,
            IsHeavyTrainingDay: isHeavyTrainingDay,
            HasNearbyMatch: hasNearbyMatch,
            HasInsufficientRest: hasInsufficientRest,
            MedicalContext: medicalContext,
            ContextNotes: contextNotes));

        var nutritionRequest = new NutritionRecommendationRequestDto(
            AthleticGoal: evaluation.AthleticGoal,
            ApprovedFoods: null,
            Moment: evaluation.DetectedMoments.Count > 0 ? string.Join(", ", evaluation.DetectedMoments) : null,
            MinProteinGrams: evaluation.MinProteinGrams,
            MaxProteinGrams: evaluation.MaxProteinGrams,
            DailyProteinRequirementGrams: evaluation.MinProteinGrams.HasValue && evaluation.MaxProteinGrams.HasValue
                ? (evaluation.MinProteinGrams.Value + evaluation.MaxProteinGrams.Value) / 2.0
                : null,
            HydrationLiters: null,
            SleepHours: null,
            IncludeQuantities: evaluation.IncludeQuantities,
            AthleteId: command.AthleteId,
            WeightKg: athleteContext?.WeightKg,
            IsMinor: command.IsMinor,
            RecommendationMode: evaluation.RecommendationMode,
            RequiresProfessionalReferral: evaluation.RequiresProfessionalReferral,
            ReferralReasons: evaluation.ReferralReasons,
            DetectedMoments: evaluation.DetectedMoments,
            MissingData: evaluation.MissingData);

        NutritionRecommendationResponseDto drafted;
        try
        {
            drafted = await aiService.GenerateNutritionRecommendationAsync(nutritionRequest, cancellationToken);
        }
        catch (DomainException)
        {
            // Fallback defensivo: si el pipeline de IA rechaza por alguna razón, usar redacción determinística segura.
            drafted = RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(nutritionRequest, "Reglas");
            var validation = RecommendationOutputValidator.ValidateNutritionDto(drafted, nutritionRequest);
            if (validation.IsValid && validation.Value is not null)
                drafted = validation.Value;
        }

        // Asegurar coherencia final con evaluación/reglas (seguridad autoritativa en backend):
        // la IA sólo redacta; números, modo y motivos de seguridad los fijan las reglas.
        var finalMin = evaluation.IncludeQuantities ? evaluation.MinProteinGrams : null;
        var finalMax = evaluation.IncludeQuantities ? evaluation.MaxProteinGrams : null;
        var finalDaily = finalMin.HasValue && finalMax.HasValue ? (finalMin.Value + finalMax.Value) / 2.0 : (double?)null;
        var finalMode = evaluation.RecommendationMode;
        var finalReferral = drafted.RequiresProfessionalReferral || evaluation.RequiresProfessionalReferral;

        var snapshot = new HabitsRecommendationSnapshotDto(
            AthleticGoal: evaluation.AthleticGoal,
            WeightKg: athleteContext?.WeightKg,
            MinProteinGrams: finalMin,
            MaxProteinGrams: finalMax,
            DailyProteinRequirementGrams: finalDaily,
            SuggestedPlateComposition: drafted.SuggestedPlateComposition,
            HydrationAndSleepGuidelines: drafted.HydrationAndSleepGuidelines,
            LegalDisclaimer: drafted.LegalDisclaimer,
            RecommendationMode: finalMode,
            IsMinor: command.IsMinor,
            RequiresProfessionalReferral: finalReferral,
            ReferralReasons: evaluation.ReferralReasons
                .Concat(drafted.ReferralReasons ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct()
                .ToList(),
            DetectedMoments: evaluation.DetectedMoments
                .Concat(drafted.DetectedMoments ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .ToList(),
            MissingData: evaluation.MissingData
                .Concat(drafted.MissingData ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .ToList(),
            MentionedFoods: drafted.MentionedFoods,
            CalculationNote: evaluation.CalculationNote);

        var entity = RecommendationMapper.CreatePendingHabitsEntity(
            athleteId: command.AthleteId,
            categoryId: primaryCategory?.Id,
            categoryName: primaryCategory?.Name,
            title: BuildTitle(evaluation),
            reason: BuildReason(evaluation),
            recommendationText: BuildRecommendationText(snapshot),
            snapshot: snapshot,
            provider: drafted.ProviderUsed);

        await recommendationRepository.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RecommendationMapper.ToHabitsResponse(entity, command.IsMinor);
    }

    private static string BuildTitle(HabitsEvaluationResult evaluation)
    {
        if (evaluation.RequiresProfessionalReferral || string.Equals(evaluation.RecommendationMode, HabitsRecommendationRules.ModeReferral, StringComparison.OrdinalIgnoreCase))
            return "Recomendación de hábitos con derivación profesional";
        if (!evaluation.IncludeQuantities)
            return "Recomendación cualitativa de hábitos y alimentación";
        return "Recomendación de hábitos y proteína";
    }

    private static string BuildReason(HabitsEvaluationResult evaluation)
    {
        var parts = new List<string> { $"Objetivo: {evaluation.AthleticGoal}." };
        if (evaluation.DetectedMoments.Count > 0)
            parts.Add($"Momentos: {string.Join(", ", evaluation.DetectedMoments)}.");
        if (evaluation.MissingData.Count > 0)
            parts.Add("Hay datos faltantes o pendientes de validación (ver detalle).");
        if (evaluation.RequiresProfessionalReferral)
            parts.Add("Se sugiere derivación profesional por seguridad.");
        var reason = string.Join(" ", parts);
        return reason.Length > AthleteRecommendation.MaxReasonLength ? reason[..AthleteRecommendation.MaxReasonLength] : reason;
    }

    private static string BuildRecommendationText(HabitsRecommendationSnapshotDto snapshot)
    {
        var parts = new List<string>
        {
            snapshot.SuggestedPlateComposition,
            snapshot.HydrationAndSleepGuidelines
        };
        if (snapshot.MinProteinGrams.HasValue && snapshot.MaxProteinGrams.HasValue)
            parts.Add($"Rango estimado de proteína diaria: {snapshot.MinProteinGrams:0.#}-{snapshot.MaxProteinGrams:0.#} g (calculado por reglas).");
        if (snapshot.ReferralReasons is { Count: > 0 })
            parts.Add($"Derivación/precaución: {string.Join("; ", snapshot.ReferralReasons)}");
        if (snapshot.MissingData is { Count: > 0 })
            parts.Add($"Datos faltantes/TODO: {string.Join("; ", snapshot.MissingData)}");
        parts.Add(snapshot.LegalDisclaimer);
        var text = string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length > AthleteRecommendation.MaxRecommendationTextLength
            ? text[..AthleteRecommendation.MaxRecommendationTextLength]
            : text;
    }
}
