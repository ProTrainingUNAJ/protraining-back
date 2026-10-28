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

        public async Task<(List<Exercise> Items, int TotalCount)> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, int page, int pageSize, CancellationToken cancellationToken = default)
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
                exercises = exercises.Where(e => EF.Functions.TrigramsAreWordSimilar(searchTerm, e.Name));
            }

            var totalCount = await exercises.CountAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                exercises = exercises.OrderByDescending(e => EF.Functions.TrigramsWordSimilarity(searchTerm, e.Name)).ThenBy(e => e.Name);
            }
            else
            {
                exercises = exercises.OrderBy(e => e.Name);
            }

            var pag = page < 1 ? 1 : page;
            var pagSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);

            exercises = exercises.Skip((pag - 1) * pagSize).Take(pagSize);

            var items = await exercises.ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await context.Exercises.AnyAsync(e => e.Id == id, cancellationToken);
        }
    }
}
