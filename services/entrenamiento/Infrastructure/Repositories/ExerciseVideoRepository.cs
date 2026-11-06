using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class ExerciseVideoRepository : IExerciseVideoRepository
    {
        private readonly AppDbContext context;

        public ExerciseVideoRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task AddAsync(ExerciseVideo video, CancellationToken cancellationToken = default)
        {
            await context.ExerciseVideos.AddAsync(video, cancellationToken);
        }

        public async Task<ExerciseVideo?> GetByExerciseIdAsync(Guid exerciseId, CancellationToken cancellationToken = default)
        {
            return await context.ExerciseVideos.FirstOrDefaultAsync(e => e.ExerciseId == exerciseId, cancellationToken);
        }

        public async Task<Dictionary<Guid, string>> GetVideosByExerciseIdsAsync(IEnumerable<Guid> exerciseIds, CancellationToken cancellationToken = default)
        {
            var idList = exerciseIds.Distinct().ToList();
            return await context.ExerciseVideos
                .Where(v => idList.Contains(v.ExerciseId))
                .ToDictionaryAsync(v => v.ExerciseId, v => v.VideoUrl, cancellationToken);
        }

        public void Remove(ExerciseVideo video)
        {
            context.ExerciseVideos.Remove(video);
        }
    }
}
