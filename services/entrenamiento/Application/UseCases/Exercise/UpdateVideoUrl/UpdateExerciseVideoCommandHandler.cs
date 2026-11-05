using Application.DTOs.Exercise;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Exercise.UpdateVideoUrl
{
    public class UpdateExerciseVideoCommandHandler
    {
        private readonly IExerciseRepository _exerciseRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UpdateExerciseVideoCommandHandler(IExerciseRepository exerciseRepository, IUnitOfWork unitOfWork)
        {
            _exerciseRepository = exerciseRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<ExerciseDto> Handle(UpdateExerciseVideoCommand request, CancellationToken cancellationToken = default)
        {
            var exercise = await _exerciseRepository.GetByIdAsync(request.ExerciseId, cancellationToken);

            if (exercise == null)
            {
                throw new NotFoundException($"No se encontró el ejercicio con id '{request.ExerciseId}'.");
            }

            exercise.UpdateVideoUrl(request.VideoUrl);

            await _unitOfWork.SaveChangesAsync();

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

            return exerciseDto;
        }
    }
}
