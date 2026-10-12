using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs.Exercise
{
    public class ExerciseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string MuscleGroup { get; set; }
        public string? Description { get; set; }
        public string? AnimationUrl { get; set; }
        public string? VideoUrl { get; set; }
        public string? Equipment { get; set; }
    }
}
