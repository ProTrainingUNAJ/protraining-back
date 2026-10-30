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

        public async Task<(List<Exercise> Items, int TotalCount)> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, bool usedByPf, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var exercises = ApplyFilters(context.Exercises.AsNoTracking(), searchTerm, equipment, muscleGroup, usedByPf);

            var totalCount = await exercises.CountAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                exercises = exercises.OrderByDescending(e => EF.Functions.TrigramsWordSimilarity(searchTerm, e.Name)).ThenBy(e => e.Name);
            }
            else
            {
                exercises = exercises.OrderBy(e => e.Name);
            }

            var items = await Paginate(exercises, page, pageSize).ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<(List<Exercise> Items, int TotalCount)> ListBasesAsync(string? searchTerm, string? equipment, string? muscleGroup, bool usedByPf, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var matches = ApplyFilters(context.Exercises.AsNoTracking(), searchTerm, equipment, muscleGroup, usedByPf);

            // Un base entra si coincide él o alguna de sus variantes.
            var bases = context.Exercises.AsNoTracking()
                .Where(e => e.ParentExerciseId == null
                    && matches.Any(m => m.Id == e.Id || m.ParentExerciseId == e.Id));

            var totalCount = await bases.CountAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                // Se ordena por la mejor similitud de la familia (el base o cualquiera de sus variantes).
                bases = bases
                    .OrderByDescending(e => matches
                        .Where(m => m.Id == e.Id || m.ParentExerciseId == e.Id)
                        .Max(m => (double?)EF.Functions.TrigramsWordSimilarity(searchTerm, m.Name)))
                    // A igual similitud, primero las familias con más variantes (el ejercicio "principal").
                    .ThenByDescending(e => context.Exercises.Count(v => v.ParentExerciseId == e.Id))
                    .ThenBy(e => e.Name);
            }
            else
            {
                bases = bases.OrderBy(e => e.Name);
            }

            var items = await Paginate(bases, page, pageSize).ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<List<Exercise>> GetVariantsAsync(IReadOnlyCollection<Guid> baseExerciseIds, CancellationToken cancellationToken = default)
        {
            if (baseExerciseIds.Count == 0)
                return [];

            return await context.Exercises
                .AsNoTracking()
                .Where(e => e.ParentExerciseId != null && baseExerciseIds.Contains(e.ParentExerciseId.Value))
                .OrderBy(e => e.VariantLabel)
                .ThenBy(e => e.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await context.Exercises.AnyAsync(e => e.Id == id, cancellationToken);
        }

        private IQueryable<Exercise> ApplyFilters(IQueryable<Exercise> exercises, string? searchTerm, string? equipment, string? muscleGroup, bool usedByPf)
        {
            if (usedByPf)
            {
                var planExerciseIds = context.PrescribedExercises.Select(pe => pe.ExerciseId);
                var recordExerciseIds = context.AthleteRecords.Select(r => r.ExerciseId);
                var usedExerciseIds = planExerciseIds.Union(recordExerciseIds);

                exercises = exercises.Where(e => usedExerciseIds.Contains(e.Id));
            }

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

            return exercises;
        }

        private static IQueryable<Exercise> Paginate(IQueryable<Exercise> exercises, int page, int pageSize)
        {
            var pag = page < 1 ? 1 : page;
            var pagSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);

            return exercises.Skip((pag - 1) * pagSize).Take(pagSize);
        }
    }
}
