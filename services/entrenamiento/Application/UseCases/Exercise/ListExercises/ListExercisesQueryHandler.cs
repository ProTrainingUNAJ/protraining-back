using Application.DTOs.Exercise;
using Application.Interfaces.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.UseCases.Exercise.ListExercises
{
    public class ListExercisesQueryHandler
    {
        private readonly IExerciseRepository exerciseRepository;

        public ListExercisesQueryHandler(IExerciseRepository exerciseRepository)
        {
            this.exerciseRepository = exerciseRepository;
        }

        public async Task<List<ExerciseDto>> Handle(ListExercisesQuery request, CancellationToken cancellationToken = default)
        {
            var exercises = await exerciseRepository.ListAsync( request.Search, request.Equipment, request.MuscleGroup, cancellationToken);

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

            return exercisesDto;
        }
    }
}
