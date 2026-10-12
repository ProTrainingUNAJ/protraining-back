using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.UseCases.Exercise.ListExercises
{
    public class ListExercisesQuery
    {
        public string? Search { get; set; }
        public string? MuscleGroup { get; set; }
        public string? Equipment { get; set; }
    }
}
