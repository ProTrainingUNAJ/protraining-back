using Application.DTOs.Exercise;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Exercise.ListExercises
{
    public class ListExercisesQueryHandler
    {
        private const int MaxPageSize = 100;
        private const int DefaultPageSize = 20;

        private readonly IExerciseRepository exerciseRepository;

        public ListExercisesQueryHandler(IExerciseRepository exerciseRepository)
        {
            this.exerciseRepository = exerciseRepository;
        }

        public async Task<PaginatedExerciseResponse> Handle(ListExercisesQuery request, CancellationToken cancellationToken = default)
        {
            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize < 1 ? DefaultPageSize : (request.PageSize > MaxPageSize ? MaxPageSize : request.PageSize);

            var (exercises, totalCount) = await exerciseRepository.ListAsync(
                request.Search,
                request.Equipment,
                request.MuscleGroup,
                page,
                pageSize,
                cancellationToken);

            var exercisesDto = exercises.Select(e => new ExerciseDto
            {
                Id = e.Id,
                Name = e.Name,
                MuscleGroup = e.MuscleGroup,
                Description = e.Description,
                AnimationUrl = e.AnimationUrl,
                VideoUrl = e.VideoUrl,
                Equipment = e.Equipment,
                Images = e.Images,
                PrimaryMuscles = e.PrimaryMuscles,
                SecondaryMuscles = e.SecondaryMuscles,
            }).ToList();

            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PaginatedExerciseResponse(exercisesDto, page, pageSize, totalCount, totalPages);
        }
    }
}
