using System.Text.Json;
using Application.DTOs.Routines;
using Application.DTOs.TrainingSessions;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Domain.Services;

namespace Application.UseCases.Routines.GetTodayRoutine
{
    public class GetTodayRoutineQueryHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly IAthleteRecordRepository athleteRecordRepository;
        private readonly ITrainingSessionRepository trainingSessionRepository;
        private readonly IExerciseVideoRepository exerciseVideoRepository;
        private readonly IAthleteSessionAdjustmentRepository? adjustmentRepository;
        private readonly IExerciseRepository? exerciseRepository;
        private readonly IPerformedSetRepository performedSetRepository;
        private readonly IAthleteVariantRepository? athleteVariantRepository;
        private readonly ILoadCalculatorEngine calculator;
        private readonly IStructureClient structureClient;
        private readonly ITrackingClient? trackingClient;
        private readonly IExerciseMediaCatalog? exerciseMediaCatalog;

        public GetTodayRoutineQueryHandler(
            IAthleteRecordRepository athleteRecordRepository,
            ITrainingSessionRepository trainingSessionRepository,
            ILoadCalculatorEngine calculator,
            IStructureClient structureClient,
            IExerciseVideoRepository exerciseVideoRepository,
            IPerformedSetRepository performedSetRepository,
            IAthleteSessionAdjustmentRepository? adjustmentRepository = null,
            IExerciseRepository? exerciseRepository = null,
            IExerciseMediaCatalog? exerciseMediaCatalog = null,
            IAthleteVariantRepository? athleteVariantRepository = null,
            ITrackingClient? trackingClient = null)
        {
            this.athleteRecordRepository = athleteRecordRepository;
            this.trainingSessionRepository = trainingSessionRepository;
            this.calculator = calculator;
            this.structureClient = structureClient;
            this.exerciseVideoRepository = exerciseVideoRepository;
            this.adjustmentRepository = adjustmentRepository;
            this.exerciseRepository = exerciseRepository;
            this.performedSetRepository = performedSetRepository;
            // Sin catálogo de ilustraciones (KAN-549), la rutina se ve con las fotos de siempre.
            this.exerciseMediaCatalog = exerciseMediaCatalog;
            this.athleteVariantRepository = athleteVariantRepository;
            this.trackingClient = trackingClient;
        }

        public async Task<TodayRoutineResponseDto> Handle(GetTodayRoutineQuery query, CancellationToken cancellationToken = default)
        {
            // Fecha pedida (hora Argentina); sin fecha, la de hoy
            var date = query.Date ?? ArgentinaTime.Today;

            Guid athleteId;
            string athleteName = string.Empty;
            IReadOnlyList<Guid> categoryIds = [];

            if (query.AthleteId.HasValue && query.AthleteId.Value != Guid.Empty)
            {
                athleteId = query.AthleteId.Value;

                var athlete = await structureClient.GetAthleteByIdAsync(athleteId, cancellationToken)
                    ?? throw new NotFoundException("El atleta no existe o no es tuyo");

                athleteName = athlete.FullName;

                var categories = await structureClient.GetAthleteCategoriesAsync(athleteId, cancellationToken);

                categoryIds = categories?.Select(c => c.Id).ToList() ?? [];
            }
            else
            {
                var athlete = await structureClient.GetMeAsync(cancellationToken) ?? throw new NotFoundException("No se encontró un perfil de atleta vinculado a la cuenta actual.");

                athleteId = athlete.AthleteId;

                athleteName = athlete.FullName;

                categoryIds = athlete.Categories.Select(c => c.Id).ToList();
            }

            var routine = await trainingSessionRepository.GetByAthleteIdAsync(athleteId, categoryIds, date, cancellationToken);

            if (routine == null)
            {
                return new TodayRoutineResponseDto
                {
                    AthleteId = athleteId,
                    AthleteName = athleteName,
                    Date = date,
                    SessionId = null,
                    HasActiveInjuries = false,
                    RoutineAdapted = false,
                    AdjustedByCoach = false,
                    AdjustmentNote = null,
                    Exercises = []
                };
            }

            // Resolver si el PF aprobó un ajuste del día para este atleta y sesión (KAN-452, KAN-478)
            var replacementsByOriginal = new Dictionary<Guid, Guid>();
            var substituteExercisesById = new Dictionary<Guid, Domain.Entities.Exercise>();
            var variantReasons = new Dictionary<Guid, string?>();
            bool adjustedByCoach = false;
            string? adjustmentNote = null;
            AthleteSessionAdjustment? activeAdjustment = null;

            bool adaptedByVariant = false;
            if (athleteVariantRepository != null)
            {
                var activeVariants = await athleteVariantRepository.GetActiveApprovedByAthleteAsync(athleteId, date, cancellationToken);

                foreach (var variant in activeVariants)
                {
                    if (variant.OriginalExerciseId != Guid.Empty && variant.SubstituteExerciseId != Guid.Empty)
                    {
                        replacementsByOriginal[variant.OriginalExerciseId] = variant.SubstituteExerciseId;
                        variantReasons[variant.OriginalExerciseId] = variant.Note;
                        adaptedByVariant = true;
                    }
                }
            }

            if (adjustmentRepository != null)
            {
                activeAdjustment = await adjustmentRepository.GetBySessionAthleteAndDateAsNoTrackingAsync(
                    routine.Id,
                    athleteId,
                    date,
                    cancellationToken);

                if (activeAdjustment != null)
                {
                    var parsedItems = ParseAdjustmentItems(activeAdjustment.Items);
                    if (parsedItems.Count > 0 || activeAdjustment.LoadReductionPercentage.HasValue || !string.IsNullOrWhiteSpace(activeAdjustment.Note))
                    {
                        adjustedByCoach = true;
                        adjustmentNote = activeAdjustment.Note;

                        foreach (var item in parsedItems)
                        {
                            if (item.OriginalExerciseId != Guid.Empty && item.SubstituteExerciseId != Guid.Empty)
                            {
                                replacementsByOriginal[item.OriginalExerciseId] = item.SubstituteExerciseId;
                                variantReasons[item.OriginalExerciseId] = string.IsNullOrWhiteSpace(activeAdjustment.Note)
                                                                          ? "Ajuste aplicado por tu PF para la sesión de hoy."
                                                                          : activeAdjustment.Note.Trim();
                            }
                        }
                    }
                }
            }

            if (exerciseRepository != null && replacementsByOriginal.Count > 0)
            {
                var substituteIds = replacementsByOriginal.Values.Distinct().ToList();
                var loadedSubstitutes = await exerciseRepository.ListByIdsAsync(substituteIds, cancellationToken);
                substituteExercisesById = loadedSubstitutes.ToDictionary(e => e.Id);
            }

            var exercisesDto = new List<TodayRoutineExerciseDto>();

            var effectiveExerciseIds = routine.PrescribedExercises
                .Select(p => replacementsByOriginal.TryGetValue(p.ExerciseId, out var subId) ? subId : p.ExerciseId)
                .Distinct()
                .ToList();

            var pfVideos = await exerciseVideoRepository.GetVideosByExerciseIdsAsync(effectiveExerciseIds, cancellationToken);

            var performedSets = performedSetRepository != null
                ? (await performedSetRepository.GetByAthleteAndDateAsync(athleteId, date, cancellationToken) ?? [])
                : [];

            var hasTimedExercise = routine.PrescribedExercises.Any(p =>
            {
                var ex = replacementsByOriginal.TryGetValue(p.ExerciseId, out var subId)
                    && substituteExercisesById.TryGetValue(subId, out var sub)
                        ? sub
                        : p.Exercise;
                return ex?.ExerciseType is ExerciseType.Distance or ExerciseType.Time;
            });

            var bestMarksByExerciseName = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (hasTimedExercise && trackingClient is not null)
            {
                var marks = await trackingClient.GetAthleteBestMarksAsync(athleteId, cancellationToken);
                foreach (var mark in marks)
                {
                    if (!string.IsNullOrWhiteSpace(mark.TargetExerciseName))
                        bestMarksByExerciseName.TryAdd(mark.TargetExerciseName.Trim(), mark.BestValue);
                }
            }

            // Por cada ejercicio prescrito (o su sustituto si fue ajustado hoy), resolvemos la carga a partir de su 1RM
            foreach (var prescribed in routine.PrescribedExercises)
            {
                var effectiveExerciseId = prescribed.ExerciseId;
                var effectiveExercise = prescribed.Exercise;
                bool isAdapted = false;
                string? originalExerciseName = null;
                string? adaptationReason = null;

                if (replacementsByOriginal.TryGetValue(prescribed.ExerciseId, out var substituteId))
                {
                    effectiveExerciseId = substituteId;
                    substituteExercisesById.TryGetValue(substituteId, out effectiveExercise);
                    isAdapted = true;
                    originalExerciseName = prescribed.Exercise?.Name;
                    variantReasons.TryGetValue(prescribed.ExerciseId, out adaptationReason);
                }

                var exerciseType = effectiveExercise?.ExerciseType ?? ExerciseType.Weight;
                bool isTimedExercise = exerciseType is ExerciseType.Distance or ExerciseType.Time;

                var activeRecord = isTimedExercise
                    ? null
                    : await athleteRecordRepository.GetActiveRecordAsync(athleteId, effectiveExerciseId, cancellationToken);

                decimal percentage = prescribed.TargetPercentage ?? 100m;
                if (activeAdjustment?.LoadReductionPercentage is { } reduction && reduction > 0)
                {
                    percentage = Math.Round(percentage * (100m - reduction) / 100m, 2);
                    var reductionText = $"Reducción de carga del {reduction}%";
                    adaptationReason = string.IsNullOrWhiteSpace(adaptationReason)
                        ? (string.IsNullOrWhiteSpace(activeAdjustment.Note)
                            ? reductionText
                            : $"{activeAdjustment.Note.Trim()} ({reductionText})")
                        : $"{adaptationReason} · {reductionText}";
                }

                double? targetTime = null;

                double? bestMark = null;

                if (isTimedExercise)
                {
                    // Si el ejercicio tiene test/marca de tiempo
                    decimal? bestTestTime = effectiveExercise?.Name is { } exerciseName
                        && bestMarksByExerciseName.TryGetValue(exerciseName.Trim(), out var mark)
                        ? mark
                        : null;

                    var timeResult = calculator.CalculateTimeTarget(bestTestTime, percentage);

                    targetTime = timeResult.HasValue ? (double)timeResult.Value : null;

                    bestMark = bestTestTime.HasValue ? (double)bestTestTime.Value : null;
                }

                decimal? current1RmKg = isTimedExercise ? null : activeRecord?.Weight;

                var source = isTimedExercise ? null : activeRecord?.Source;

                var loadResult = calculator.CalculateLoad(current1RmKg, source, percentage);

                bool isBarbell = Equipment.IsBarbell(effectiveExercise?.Equipment);

                double? barWeight = isBarbell ? 20 : null;

                double? totalWeight = loadResult.WeightKg.HasValue ? (double)loadResult.WeightKg.Value : null;

                double? weightPerSide = isBarbell ? calculator.CalculateWeightPerSide(totalWeight, barWeight!.Value) : null;

                var effectiveVideoUrl = pfVideos.TryGetValue(effectiveExerciseId, out var customVideo)
                    ? customVideo
                    : effectiveExercise?.VideoUrl;

                exercisesDto.Add(new TodayRoutineExerciseDto
                {
                    ExerciseId = effectiveExerciseId,
                    Name = effectiveExercise?.Name ?? string.Empty,
                    MuscleGroup = effectiveExercise?.MuscleGroup ?? string.Empty,
                    VideoUrl = effectiveVideoUrl,
                    Images = effectiveExercise?.Images.ToList() ?? [],
                    // Ilustraciones del catálogo (KAN-549): solo para ejercicios del catálogo global.
                    Media = effectiveExercise is { Origin: Origin.Catalog } && effectiveExercise.PfId == Guid.Empty && exerciseMediaCatalog is not null
                        ? exerciseMediaCatalog.GetForCatalogImages(effectiveExercise.Images).ToList()
                        : [],
                    PrimaryMuscles = effectiveExercise?.PrimaryMuscles.ToList() ?? [],
                    SecondaryMuscles = effectiveExercise?.SecondaryMuscles.ToList() ?? [],
                    ExerciseType = exerciseType,
                    Sets = prescribed.Sets,
                    Reps = int.TryParse(prescribed.Reps, out var parsedReps) ? parsedReps : (int.TryParse(prescribed.Reps.Split('-')[0].Trim(), out var minReps) ? minReps : 0),
                    RepsRange = prescribed.Reps,
                    Percentage = (double)percentage,
                    WeightKg = totalWeight,
                    BarWeight = barWeight,
                    WeightPerSide = weightPerSide,
                    Current1RmKg = current1RmKg.HasValue ? (double)current1RmKg.Value : null,
                    IsReference = loadResult.IsReference,
                    TargetTimeSeconds = targetTime,
                    BestMarkSeconds = bestMark,
                    PerformedSets = performedSets.Where(p => p.ExerciseId == effectiveExerciseId).OrderBy(p => p.SetNumber ?? 0)
                    .Select(p => new TodayRoutinePerformedSetDto
                    {
                        SetNumber = p.SetNumber,
                        Weight = p.Weight,
                        Reps = p.Reps,
                        TimeSeconds = p.TimeSeconds
                    })
                    .ToList(),
                    IsAdapted = isAdapted,
                    OriginalExerciseName = originalExerciseName,
                    AdaptationReason = adaptationReason
                });
            }

            var effectiveCoachNotes = adjustedByCoach && !string.IsNullOrWhiteSpace(adjustmentNote)
                ? (string.IsNullOrWhiteSpace(routine.CoachNotes) ? adjustmentNote : $"{routine.CoachNotes} | {adjustmentNote}")
                : routine.CoachNotes;

            var todayRoutineDto = new TodayRoutineResponseDto
            {
                AthleteId = athleteId,
                AthleteName = athleteName,
                Date = date,
                SessionId = routine.Id,
                HasActiveInjuries = false,
                RoutineAdapted = adjustedByCoach || adaptedByVariant,
                AdjustedByCoach = adjustedByCoach,
                AdjustmentNote = adjustmentNote,
                CoachNotes = effectiveCoachNotes,
                StartTime = routine.StartTime,
                Exercises = exercisesDto
            };

            return todayRoutineDto;
        }

        private static IReadOnlyList<AthleteSessionAdjustmentItemDto> ParseAdjustmentItems(string itemsJson)
        {
            if (string.IsNullOrWhiteSpace(itemsJson))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<AthleteSessionAdjustmentItemDto>>(itemsJson, JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }
    }
}
