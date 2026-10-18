using Application.DTOs.Routines;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Services;

namespace Application.UseCases.Routines.GetTodayRoutine
{
    public class GetTodayRoutineQueryHandler
    {
        private readonly IAthleteRecordRepository athleteRecordRepository;
        private readonly ITrainingSessionRepository trainingSessionRepository;
        private readonly ILoadCalculatorEngine calculator;
        private readonly IStructureClient structureClient;

        public GetTodayRoutineQueryHandler(IAthleteRecordRepository athleteRecordRepository, ITrainingSessionRepository trainingSessionRepository, ILoadCalculatorEngine calculator, IStructureClient structureClient)
        {
            this.athleteRecordRepository = athleteRecordRepository;
            this.trainingSessionRepository = trainingSessionRepository;
            this.calculator = calculator;
            this.structureClient = structureClient;
        }

        public async Task<TodayRoutineResponseDto> Handle(GetTodayRoutineQuery query, CancellationToken cancellationToken = default)
        {
            Guid athleteId;
            string athleteName = string.Empty;
            IReadOnlyList<Guid> categoryIds = [];

            if (query.AthleteId.HasValue && query.AthleteId.Value != Guid.Empty)
            {
                athleteId = query.AthleteId.Value;

                var categories = await structureClient.GetAthleteCategoriesAsync(athleteId, cancellationToken);

                categoryIds = categories?.Select(c => c.Id).ToList() ?? [];

                var athlete = await structureClient.GetAthleteByIdAsync(athleteId, cancellationToken);

                if (athlete != null)
                {
                    athleteName = athlete.FullName;
                }
            }
            else
            {
                var athlete = await structureClient.GetMeAsync(cancellationToken) ?? throw new NotFoundException("No se encontró un perfil de atleta vinculado a la cuenta actual.");

                athleteId = athlete.AthleteId;

                athleteName = athlete.FullName;

                categoryIds = athlete.Categories.Select(c => c.Id).ToList();
            }

            var routine = await trainingSessionRepository.GetByAthleteIdAsync(athleteId, categoryIds, cancellationToken);

            if(routine == null)
            {
                return new TodayRoutineResponseDto
                {
                    AthleteId = athleteId,
                    AthleteName = athleteName,
                    Date = ArgentinaTime.Today,
                    HasActiveInjuries = false,
                    RoutineAdapted = false,
                    Exercises = []
                };
            }

            var exercisesDto = new List<TodayRoutineExerciseDto>();

            // Por cada ejercicio prescrito, resolvemos la carga a partir de su 1RM
            foreach (var prescribed in routine.PrescribedExercises)
            {
                var activeRecord = await athleteRecordRepository.GetActiveRecordAsync(athleteId, prescribed.ExerciseId, cancellationToken);

                decimal percentage = prescribed.TargetPercentage ?? 100m;

                decimal? current1RmKg = activeRecord?.Weight;

                var source = activeRecord?.Source;

                var loadResult = calculator.CalculateLoad(current1RmKg, source, percentage);

                var exercise = prescribed.Exercise;

                exercisesDto.Add(new TodayRoutineExerciseDto
                {
                    ExerciseId = prescribed.ExerciseId,
                    Name = exercise?.Name ?? string.Empty,
                    MuscleGroup = exercise?.MuscleGroup ?? string.Empty,
                    VideoUrl = exercise?.VideoUrl,
                    Sets = prescribed.Sets,
                    Reps = prescribed.Reps,
                    Percentage = (double)percentage,
                    WeightKg = loadResult.WeightKg.HasValue ? (double)loadResult.WeightKg.Value : null,
                    Current1RmKg = current1RmKg.HasValue ? (double)current1RmKg.Value : null,
                    IsReference = loadResult.IsReference
                });
            }

            var todayRoutineDto = new TodayRoutineResponseDto
            {
                AthleteId = athleteId,
                AthleteName = athleteName,
                Date = ArgentinaTime.Today,
                HasActiveInjuries = false,
                RoutineAdapted = false,
                CoachNotes = routine.CoachNotes,
                Exercises = exercisesDto
            };

            return todayRoutineDto;
        }
    }
}
