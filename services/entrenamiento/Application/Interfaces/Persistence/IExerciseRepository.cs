using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Application.Interfaces.Persistence
{
    public interface IExerciseRepository
    {
        Task<List<Exercise>> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, CancellationToken cancellationToken = default);
    }
}