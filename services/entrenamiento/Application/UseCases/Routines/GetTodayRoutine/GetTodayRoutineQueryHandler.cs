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
        private readonly ILoadCalculatorEngine calculator;
        private readonly IStructureClient structureClient;

        public GetTodayRoutineQueryHandler(
            IAthleteRecordRepository athleteRecordRepository,
            ITrainingSessionRepository trainingSessionRepository,
            ILoadCalculatorEngine calculator,
            IStructureClient structureClient,
            IExerciseVideoRepository exerciseVideoRepository,
            IAthleteSessionAdjustmentRepository? adjustmentRepository = null,
            IExerciseRepository? exerciseRepository = null)
        {
            this.athleteRecordRepository = athleteRecordRepository;
            this.trainingSessionRepository = trainingSessionRepository;
            this.calculator = calculator;
            this.structureClient = structureClient;
            this.exerciseVideoRepository = exerciseVideoRepository;
            this.adjustmentRepository = adjustmentRepository;
            this.exerciseRepository = exerciseRepository;
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

            // Resolver si el PF aprobó un ajuste del día para este atleta y sesión (KAN-452)
            var replacementsByOriginal = new Dictionary<Guid, Guid>();
            var substituteExercisesById = new Dictionary<Guid, Domain.Entities.Exercise>();
            bool adjustedByCoach = false;
            string? adjustmentNote = null;

            if (adjustmentRepository != null)
            {
                var adjustment = await adjustmentRepository.GetBySessionAthleteAndDateAsNoTrackingAsync(
                    routine.Id,
                    athleteId,
                    date,
                    cancellationToken);

                if (adjustment != null)
                {
                    var parsedItems = ParseAdjustmentItems(adjustment.Items);
                    if (parsedItems.Count > 0)
                    {
                        adjustedByCoach = true;
                        adjustmentNote = adjustment.Note;

                        foreach (var item in parsedItems)
                        {
                            if (item.OriginalExerciseId != Guid.Empty && item.SubstituteExerciseId != Guid.Empty)
                            {
                                replacementsByOriginal[item.OriginalExerciseId] = item.SubstituteExerciseId;
                            }
                        }

                        if (exerciseRepository != null && replacementsByOriginal.Count > 0)
                        {
                            var substituteIds = replacementsByOriginal.Values.Distinct().ToList();
                            var loadedSubstitutes = await exerciseRepository.ListByIdsAsync(substituteIds, cancellationToken);
                            substituteExercisesById = loadedSubstitutes.ToDictionary(e => e.Id);
                        }
                    }
                }
            }

            var exercisesDto = new List<TodayRoutineExerciseDto>();

            var effectiveExerciseIds = routine.PrescribedExercises
                .Select(p => replacementsByOriginal.TryGetValue(p.ExerciseId, out var subId) ? subId : p.ExerciseId)
                .Distinct()
                .ToList();

            var pfVideos = await exerciseVideoRepository.GetVideosByExerciseIdsAsync(effectiveExerciseIds, cancellationToken);

            // Por cada ejercicio prescrito (o su sustituto si fue ajustado hoy), resolvemos la carga a partir de su 1RM
            foreach (var prescribed in routine.PrescribedExercises)
            {
                var effectiveExerciseId = prescribed.ExerciseId;
                var effectiveExercise = prescribed.Exercise;

                if (replacementsByOriginal.TryGetValue(prescribed.ExerciseId, out var substituteId))
                {
                    effectiveExerciseId = substituteId;
                    substituteExercisesById.TryGetValue(substituteId, out effectiveExercise);
                }

                var activeRecord = await athleteRecordRepository.GetActiveRecordAsync(athleteId, effectiveExerciseId, cancellationToken);

                decimal percentage = prescribed.TargetPercentage ?? 100m;

                decimal? current1RmKg = activeRecord?.Weight;

                var source = activeRecord?.Source;

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
                    PrimaryMuscles = effectiveExercise?.PrimaryMuscles.ToList() ?? [],
                    SecondaryMuscles = effectiveExercise?.SecondaryMuscles.ToList() ?? [],
                    Sets = prescribed.Sets,
                    Reps = prescribed.Reps,
                    Percentage = (double)percentage,
                    WeightKg = totalWeight,
                    BarWeight = barWeight,
                    WeightPerSide = weightPerSide,
                    Current1RmKg = current1RmKg.HasValue ? (double)current1RmKg.Value : null,
                    IsReference = loadResult.IsReference
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
                RoutineAdapted = adjustedByCoach,
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
