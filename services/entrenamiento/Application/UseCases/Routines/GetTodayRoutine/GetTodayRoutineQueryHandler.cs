using Application.DTOs.Routines;
using Application.Interfaces.Persistence;
using Domain.Services;

namespace Application.UseCases.Routines.GetTodayRoutine
{
    public class GetTodayRoutineQueryHandler
    {
        private readonly IAthleteRecordRepository athleteRecordRepository;
        private readonly ITrainingSessionRepository trainingSessionRepository;
        private readonly ILoadCalculatorEngine calculator;

        public GetTodayRoutineQueryHandler(IAthleteRecordRepository athleteRecordRepository, ITrainingSessionRepository trainingSessionRepository, ILoadCalculatorEngine calculator)
        {
            this.athleteRecordRepository = athleteRecordRepository;
            this.trainingSessionRepository = trainingSessionRepository;
            this.calculator = calculator;
        }

        public async Task<TodayRoutineResponseDto> Handle(GetTodayRoutineQuery query, CancellationToken cancellationToken = default)
        {
            var routine = await trainingSessionRepository.GetTodaySessionByAthleteIdAsync(query.AthleteId, cancellationToken);

            if(routine == null)
            {
                return new TodayRoutineResponseDto
                {
                    AthleteId = query.AthleteId,
                    Date = DateOnly.FromDateTime(DateTime.UtcNow),
                    HasActiveInjuries = false,
                    RoutineAdapted = false,
                    Exercises = []
                };
            }

            var exercisesDto = new List<TodayRoutineExerciseDto>();

            // Por cada ejercicio prescrito, resolvemos la carga a partir de su 1RM
            foreach (var prescribed in routine.PrescribedExercises)
            {
                var activeRecord = await athleteRecordRepository.GetActiveRecordAsync(query.AthleteId, prescribed.ExerciseId, cancellationToken);

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
                    WeightKg = (double)(loadResult.WeightKg ?? 0m),
                    Current1RmKg = (double)(current1RmKg ?? 0m)
                });
            }

            var todayRoutineDto = new TodayRoutineResponseDto
            {
                AthleteId = query.AthleteId,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                HasActiveInjuries = false,
                RoutineAdapted = false,
                Exercises = exercisesDto
            };

            return todayRoutineDto;
        }
    }
}
