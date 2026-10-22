using Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class Exercise : BaseEntity
    {
        public string Name { get; private set; } = string.Empty;
        public string MuscleGroup { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public string? AnimationUrl { get; private set; }
        public string? VideoUrl { get; private set; }
        public string? Equipment { get; private set; }
        public List<string> Images { get; private set; } = [];
        public List<string> PrimaryMuscles { get; private set; } = [];
        public List<string> SecondaryMuscles { get; private set; } = [];

        private Exercise() { }
        public static Exercise Create(
            string name,
            string muscleGroup,
            string? description = null,
            string? animationUrl = null,
            string? videoUrl = null,
            string? equipment = null,
            List<string>? images = null,
            List<string>? primaryMuscles = null,
            List<string>? secondaryMuscles = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("El nombre del ejercicio no puede estar vacío.");
            if (string.IsNullOrWhiteSpace(muscleGroup))
                throw new ArgumentException("El grupo muscular es obligatorio.");

            return new Exercise
            {
                Name = name.Trim(),
                MuscleGroup = muscleGroup.Trim(),
                Description = description?.Trim(),
                AnimationUrl = animationUrl?.Trim() ?? images?.FirstOrDefault(),
                VideoUrl = videoUrl?.Trim(),
                Equipment = equipment?.Trim(),
                Images = images ?? [],
                PrimaryMuscles = primaryMuscles ?? [],
                SecondaryMuscles = secondaryMuscles ?? []
            };
        }
    }
}
