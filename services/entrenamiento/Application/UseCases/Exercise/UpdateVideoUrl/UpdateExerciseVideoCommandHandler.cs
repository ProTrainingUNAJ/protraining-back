using Application.DTOs.Exercise;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Exercise.UpdateVideoUrl
{
    public class UpdateExerciseVideoCommandHandler
    {
        private readonly IExerciseRepository _exerciseRepository;
        private readonly IExerciseVideoRepository _exerciseVideoRepository;
        private readonly IUnitOfWork _unitOfWork;

        public UpdateExerciseVideoCommandHandler(IExerciseRepository exerciseRepository, IExerciseVideoRepository exerciseVideoRepository, IUnitOfWork unitOfWork)
        {
            _exerciseRepository = exerciseRepository;
            _exerciseVideoRepository = exerciseVideoRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<ExerciseDto> Handle(UpdateExerciseVideoCommand request, CancellationToken cancellationToken = default)
        {
            var exercise = await _exerciseRepository.GetByIdAsync(request.ExerciseId, cancellationToken);

            if (exercise == null)
            {
                throw new NotFoundException($"No se encontró el ejercicio con id '{request.ExerciseId}'.");
            }

            var existingVideo = await _exerciseVideoRepository.GetByExerciseIdAsync(request.ExerciseId, cancellationToken);

            string? videoUrl;
            string? videoSource;

            if (string.IsNullOrWhiteSpace(request.VideoUrl))
            {
                if (existingVideo != null)
                {
                    _exerciseVideoRepository.Remove(existingVideo);
                }
                videoUrl = exercise.VideoUrl;
                videoSource = string.IsNullOrEmpty(videoUrl) ? null : "Catalog";
            }
            else
            {
                if (existingVideo != null)
                {
                    existingVideo.UpdateVideoUrl(request.VideoUrl);
                    videoUrl = existingVideo.VideoUrl;
                }
                else
                {
                    var newVideo = ExerciseVideo.Create(request.ExerciseId, request.VideoUrl);
                    await _exerciseVideoRepository.AddAsync(newVideo, cancellationToken);
                    videoUrl = newVideo.VideoUrl;
                }
                videoSource = "Pf";
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var exerciseDto = new ExerciseDto
            {
                Id = exercise.Id,
                Name = exercise.Name,
                MuscleGroup = exercise.MuscleGroup,
                Description = exercise.Description,
                AnimationUrl = exercise.AnimationUrl,
                VideoUrl = videoUrl,
                VideoSource = videoSource,
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
