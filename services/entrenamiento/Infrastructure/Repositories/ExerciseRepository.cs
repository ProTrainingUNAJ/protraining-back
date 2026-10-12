using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public class ExerciseRepository : IExerciseRepository
    {
        private readonly AppDbContext context;

        public ExerciseRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task<List<Exercise>> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, CancellationToken cancellationToken = default)
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
                exercises = exercises.Where(e =>
                    EF.Functions.TrigramsAreSimilar(e.Name, searchTerm) ||
                    EF.Functions.ILike(e.Name, $"%{searchTerm}%"));
            }

            return await exercises.ToListAsync(cancellationToken);
        }
    }
}
