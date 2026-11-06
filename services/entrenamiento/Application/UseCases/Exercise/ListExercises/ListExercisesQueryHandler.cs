using Application.DTOs.Exercise;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Exercise.ListExercises
{
    public class ListExercisesQueryHandler
    {
        private const int MaxPageSize = 100;
        private const int DefaultPageSize = 20;

        private readonly IExerciseRepository exerciseRepository;
        private readonly IExerciseVideoRepository exerciseVideoRepository;

        public ListExercisesQueryHandler(IExerciseRepository exerciseRepository, IExerciseVideoRepository exerciseVideoRepository)
        {
            this.exerciseRepository = exerciseRepository;
            this.exerciseVideoRepository = exerciseVideoRepository;
        }

        public async Task<PaginatedExerciseResponse> Handle(ListExercisesQuery request, CancellationToken cancellationToken = default)
        {
            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize < 1 ? DefaultPageSize : (request.PageSize > MaxPageSize ? MaxPageSize : request.PageSize);

            List<ExerciseDto> exercisesDto;
            int totalCount;

            if (request.Ids is { Count: > 0 })
            {
                // KAN-414: el Asistente verifica que los ejercicios de un borrador existan en el catálogo.
                var ids = request.Ids.Distinct().Take(MaxPageSize).ToList();
                var found = await exerciseRepository.ListByIdsAsync(ids, cancellationToken);
                exercisesDto = found.Select(ToDto).ToList();
                return new PaginatedExerciseResponse(exercisesDto, 1, MaxPageSize, exercisesDto.Count, 1);
            };

            if (request.GroupVariants)
            {
                // KAN-554: los ejercicios base de la página, cada uno con todas sus variantes.
                var (bases, total) = await exerciseRepository.ListBasesAsync(
                    request.Search,
                    request.Equipment,
                    request.MuscleGroup,
                    request.Muscle,
                    request.ExerciseType,
                    request.UsedByPf,
                    page,
                    pageSize,
                    cancellationToken);

                var variants = await exerciseRepository.GetVariantsAsync(
                    bases.Select(b => b.Id).ToList(), cancellationToken);
                var variantsByBase = variants
                    .GroupBy(v => v.ParentExerciseId!.Value)
                    .ToDictionary(g => g.Key, g => g.Select(ToDto).ToList());

                exercisesDto = bases.Select(b =>
                {
                    var dto = ToDto(b);
                    dto.Variants = variantsByBase.GetValueOrDefault(b.Id) ?? [];
                    return dto;
                }).ToList();
                totalCount = total;
            }
            else
            {
                var (exercises, total) = await exerciseRepository.ListAsync(
                    request.Search,
                    request.Equipment,
                    request.MuscleGroup,
                    request.Muscle,
                    request.ExerciseType,
                    request.UsedByPf,
                    page,
                    pageSize,
                    cancellationToken);

                exercisesDto = exercises.Select(ToDto).ToList();
                totalCount = total;
            }

            var allIds = exercisesDto
                .Select(e => e.Id)
                .Concat(exercisesDto.SelectMany(e => e.Variants ?? []).Select(v => v.Id))
                .Distinct()
                .ToList();

            var pfVideos = await exerciseVideoRepository.GetVideosByExerciseIdsAsync(allIds, cancellationToken)
                ?? new Dictionary<Guid, string>();

            void ApplyVideo(ExerciseDto dto)
            {
                if (pfVideos.TryGetValue(dto.Id, out var pfVideoUrl))
                {
                    dto.VideoUrl = pfVideoUrl;
                    dto.VideoSource = "Pf";
                }
                else
                {
                    dto.VideoSource = string.IsNullOrEmpty(dto.VideoUrl) ? null : "Catalog";
                }

                if (dto.Variants != null)
                {
                    foreach (var v in dto.Variants)
                    {
                        ApplyVideo(v);
                    }
                }
            }

            foreach (var dto in exercisesDto)
            {
                ApplyVideo(dto);
            }

            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PaginatedExerciseResponse(exercisesDto, page, pageSize, totalCount, totalPages);
        }

        private static ExerciseDto ToDto(Domain.Entities.Exercise e) => new()
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
            ParentExerciseId = e.ParentExerciseId,
            VariantLabel = e.VariantLabel,
            Origin = e.Origin,
            ExerciseType = e.ExerciseType
        };
    }
}
