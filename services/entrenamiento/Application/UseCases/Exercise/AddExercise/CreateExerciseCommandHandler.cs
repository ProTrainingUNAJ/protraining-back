using Application.DTOs.Exercise;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Application.Exceptions;


namespace Application.UseCases.Exercise.AddExercise
{
    public class CreateExerciseCommandHandler
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IExerciseRepository _exerciseRepository;

        public CreateExerciseCommandHandler(IUnitOfWork unitOfWork, IExerciseRepository exerciseRepository)
        {
            _unitOfWork = unitOfWork;
            _exerciseRepository = exerciseRepository;
        }

        public async Task<ExerciseDto> Handle(CreateExerciseCommand request, CancellationToken cancellationToken = default)
        {
            var existExercise = await _exerciseRepository.FindByNameAsync(request.Name, cancellationToken);

            if(existExercise != null)
            {
                throw new ConflictException($"Ya existe el ejercicio {existExercise.Id} con ese nombre.");
            }

            var exercise = Domain.Entities.Exercise.Create(
                                                 name: request.Name,
                                                 muscleGroup: request.MuscleGroup,
                                                 description: request.Instructions,
                                                 equipment: request.Equipment,
                                                 primaryMuscles: request.PrimaryMuscles,
                                                 secondaryMuscles: request.SecondaryMuscles,
                                                 origin: request.Origin ?? Origin.Coach
                                                 );

            await _exerciseRepository.AddExercise(exercise, cancellationToken);

            var exerciseDto = new ExerciseDto
            {
                Id = exercise.Id,
                Name = exercise.Name,
                MuscleGroup = exercise.MuscleGroup,
                Description = exercise.Description,
                AnimationUrl = exercise.AnimationUrl,
                VideoUrl = exercise.VideoUrl,
                Equipment = exercise.Equipment,
                Images = exercise.Images,
                PrimaryMuscles = exercise.PrimaryMuscles,
                SecondaryMuscles = exercise.SecondaryMuscles,
                Origin = exercise.Origin
            };

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return exerciseDto;
        }
    }
}
