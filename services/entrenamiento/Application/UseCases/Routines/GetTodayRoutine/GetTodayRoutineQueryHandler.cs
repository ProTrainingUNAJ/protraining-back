using Application.DTOs.Routines;
using Application.DTOs.TrainingSessions;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Domain.Services;
using System.Text.Json;

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
                    Exercises = [],
                    Blocks = [],
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

                if (activeAdjustment != null && activeAdjustment.IsRejected)
                {
                    // Un rechazo persistido mantiene la rutina base del grupo sin aplicar sustituciones ni reducción de carga (KAN-636).
                    activeAdjustment = null;
                }

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

            var prescribedExercises = routine.Blocks.Count > 0 ? routine.Blocks.OrderBy(b => b.Order)
                                                                               .SelectMany(b => b.PrescribedExercises).ToList()
                                                                               : routine.PrescribedExercises.ToList();

            var effectiveExerciseIds = prescribedExercises
                .Select(p => replacementsByOriginal.TryGetValue(p.ExerciseId, out var subId) ? subId : p.ExerciseId)
                .Distinct().ToList();

            var pfVideos = await exerciseVideoRepository.GetVideosByExerciseIdsAsync(effectiveExerciseIds, cancellationToken);

            var performedSets = await performedSetRepository.GetByAthleteAndDateAsNoTrackingAsync(athleteId, date, cancellationToken);

            var hasTimedExercise = prescribedExercises.Any(p =>
            {
                if (p.TargetTimeSeconds.HasValue)
                    return false;

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

            var blocksDto = new List<TodayRoutineBlockDto>();
            var exercisesDto = new List<TodayRoutineExerciseDto>();

            var resolutionContext = new ExerciseResolutionContext(
                athleteId,
                replacementsByOriginal,
                substituteExercisesById,
                variantReasons,
                activeAdjustment,
                pfVideos,
                performedSets,
                bestMarksByExerciseName,
                routine.Week?.WeekNumber);

            if (routine.Blocks.Count > 0)
            {
                foreach (var block in routine.Blocks.OrderBy(b => b.Order))
                {
                    var blockExercises = new List<TodayRoutineExerciseDto>();

                    foreach (var prescribed in block.PrescribedExercises.OrderBy(pe => pe.Order))
                    {
                        var resolvedExercise = await ResolveExerciseAsync(prescribed, resolutionContext, cancellationToken, block.Id);
                        blockExercises.Add(resolvedExercise);
                        exercisesDto.Add(resolvedExercise);
                    }

                    blocksDto.Add(new TodayRoutineBlockDto
                    {
                        Id = block.Id,
                        Title = block.Title,
                        Order = block.Order,
                        Exercises = blockExercises
                    });
                }
            }
            else
            {
                // Fallback por si alguna sesión legada o test unitario no cargó bloques
                foreach (var prescribed in routine.PrescribedExercises.OrderBy(pe => pe.Order))
                {
                    exercisesDto.Add(await ResolveExerciseAsync(prescribed, resolutionContext, cancellationToken));
                }

                if (exercisesDto.Count > 0)
                {
                    blocksDto.Add(new TodayRoutineBlockDto
                    {
                        Id = routine.Id,
                        Title = "Principal",
                        Order = 1,
                        Exercises = exercisesDto.ToList()
                    });
                }
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
                Exercises = exercisesDto,
                WeekNumber = routine.Week?.WeekNumber,
                Blocks = blocksDto,
                SessionName = routine.Name
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

        private async Task<TodayRoutineExerciseDto> ResolveExerciseAsync(PrescribedExercise prescribed, ExerciseResolutionContext context, CancellationToken cancellationToken, Guid? blockId = null)
        {
            // Por cada ejercicio prescrito (o su sustituto si fue ajustado hoy), resolvemos la carga a partir de su 1RM
            var effectiveExerciseId = prescribed.ExerciseId;
            var effectiveExercise = prescribed.Exercise;
            bool isAdapted = false;
            string? originalExerciseName = null;
            string? adaptationReason = null;
            
            if (context.ReplacementsByOriginal.TryGetValue(prescribed.ExerciseId, out var substituteId))
            {
                effectiveExerciseId = substituteId;
                context.SubstituteExercisesById.TryGetValue(substituteId, out effectiveExercise);
                isAdapted = true;
                originalExerciseName = prescribed.Exercise?.Name;
                context.VariantReasons.TryGetValue(prescribed.ExerciseId, out adaptationReason);
            }

            var exerciseType = effectiveExercise?.ExerciseType ?? ExerciseType.Weight;
            bool isTimedExercise = exerciseType is ExerciseType.Distance or ExerciseType.Time;
            bool isRepsExercise = exerciseType == ExerciseType.Reps;

            decimal percentage = prescribed.TargetPercentage ?? 0m;
            decimal? effectivePercentage = prescribed.TargetPercentage;

            if (context.ActiveAdjustment?.LoadReductionPercentage is { } reduction && reduction > 0)
            {
                if (effectivePercentage.HasValue)
                {
                    effectivePercentage = Math.Round(effectivePercentage.Value * (100m - reduction) / 100m, 2);
                    percentage = effectivePercentage.Value;
                }

                var reductionText = $"Reducción de carga del {reduction}%";
                adaptationReason = string.IsNullOrWhiteSpace(adaptationReason)
                    ? (string.IsNullOrWhiteSpace(context.ActiveAdjustment.Note)
                        ? reductionText
                        : $"{context.ActiveAdjustment.Note.Trim()} ({reductionText})")
                    : $"{adaptationReason} · {reductionText}";
            }

            double? targetDistanceMeters = null;
            double? targetTime = null;
            double? bestMark = null;
            decimal? current1RmKg = null;
            double? barWeight = null;
            double? totalWeight = null;
            double? weightPerSide = null;
            bool isReference = false;

            if (isTimedExercise)
            {
                targetDistanceMeters = prescribed.TargetDistanceMeters.HasValue
                    ? (double)prescribed.TargetDistanceMeters.Value
                    : null;

                if (prescribed.TargetTimeSeconds.HasValue)
                {
                    // La meta explícita del PF manda; el test queda como respaldo.
                    targetTime = (double)prescribed.TargetTimeSeconds.Value;
                }
                else
                {
                    decimal? bestTestTime = effectiveExercise?.Name is { } exerciseName
                        && context.BestMarksByExerciseName.TryGetValue(exerciseName.Trim(), out var mark)
                        ? mark
                        : null;

                    decimal testPercentage = effectivePercentage ?? 100m;
                    if (!effectivePercentage.HasValue && context.ActiveAdjustment?.LoadReductionPercentage is { } timeReduction && timeReduction > 0)
                    {
                        testPercentage = Math.Round(testPercentage * (100m - timeReduction) / 100m, 2);
                    }

                    var timeResult = calculator.CalculateTimeTarget(bestTestTime, testPercentage);
                    targetTime = timeResult.HasValue ? (double)timeResult.Value : null;
                    bestMark = bestTestTime.HasValue ? (double)bestTestTime.Value : null;
                }

                isReference = !prescribed.TargetTimeSeconds.HasValue
                    && !prescribed.TargetDistanceMeters.HasValue
                    && !targetTime.HasValue;
            }
            else if (!isRepsExercise && effectivePercentage.HasValue)
            {
                // Solo calculamos carga en kg para ejercicios de fuerza con %RM explícito.
                var activeRecord = await athleteRecordRepository.GetActiveRecordAsync(context.AthleteId, effectiveExerciseId, cancellationToken);
                current1RmKg = activeRecord?.Weight;
                var source = activeRecord?.Source;

                var loadResult = calculator.CalculateLoad(current1RmKg, source, effectivePercentage.Value);
                bool isBarbell = Equipment.IsBarbell(effectiveExercise?.Equipment);
                barWeight = isBarbell ? 20 : null;
                totalWeight = loadResult.WeightKg.HasValue ? (double)loadResult.WeightKg.Value : null;
                weightPerSide = isBarbell ? calculator.CalculateWeightPerSide(totalWeight, barWeight!.Value) : null;
                isReference = loadResult.IsReference;
            }

            var effectiveVideoUrl = context.PfVideos.TryGetValue(effectiveExerciseId, out var customVideo) ? customVideo : effectiveExercise?.VideoUrl;

            return new TodayRoutineExerciseDto
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
                IsReference = isReference,
                TargetDistanceMeters = targetDistanceMeters,
                TargetTimeSeconds = targetTime,
                BestMarkSeconds = bestMark,
                PerformedSets = context.PerformedSets.Where(p => p.ExerciseId == effectiveExerciseId).OrderBy(p => p.SetNumber ?? 0)
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
                AdaptationReason = adaptationReason,
                IntensityType = prescribed.IntensityType,
                TargetRpe = prescribed.TargetRpe,
                TargetRir = prescribed.TargetRir,
                Notes = prescribed.Notes,
                WeekNumber = context.WeekNumber,
                BlockId = blockId
            };
        }

        private sealed record ExerciseResolutionContext(
            Guid AthleteId,
            IReadOnlyDictionary<Guid, Guid> ReplacementsByOriginal,
            IReadOnlyDictionary<Guid, Domain.Entities.Exercise> SubstituteExercisesById,
            IReadOnlyDictionary<Guid, string?> VariantReasons,
            AthleteSessionAdjustment? ActiveAdjustment,
            IReadOnlyDictionary<Guid, string> PfVideos,
            IReadOnlyList<PerformedSet> PerformedSets,
            IReadOnlyDictionary<string, decimal> BestMarksByExerciseName,
            int? WeekNumber);
    }
}
