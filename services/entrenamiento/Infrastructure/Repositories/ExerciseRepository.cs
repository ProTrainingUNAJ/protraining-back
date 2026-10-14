using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class ExerciseRepository : IExerciseRepository
    {
        private readonly AppDbContext context;

        public ExerciseRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task<List<Exercise>> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var exercises = context.Exercises.AsNoTracking();
            
            if (!string.IsNullOrWhiteSpace(equipment))
            {
                exercises = exercises.Where(e => e.Equipment != null && EF.Functions.ILike(e.Equipment, $"%{equipment}%"));
            }
            
            if (!string.IsNullOrWhiteSpace(muscleGroup))
            {
                exercises = exercises.Where(e => e.MuscleGroup.ToLower() == muscleGroup.ToLower());
            }
           
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                exercises = exercises.Where(e => EF.Functions.TrigramsWordSimilarity(searchTerm, e.Name) > 0.3).OrderByDescending(e => EF.Functions.TrigramsWordSimilarity(searchTerm, e.Name));
            }
            else
            {
                exercises = exercises.OrderBy(e => e.Name);
            }

            var pag = page < 1 ? 1 : page;
            var pagSize = pageSize < 1 ? 20 : pageSize;

            exercises = exercises.Skip((pag - 1) * pagSize).Take(pagSize);

            return await exercises.ToListAsync(cancellationToken);
        }
    }
}
