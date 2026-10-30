using Domain.Common;
using Domain.Exceptions;
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

        // Variantes (KAN-554): una variante es un ejercicio "hijo" de su ejercicio base
        // (por ejemplo "Agarre cerrado" de "Press de banca con barra"). Conserva su id,
        // así las planificaciones, los récords (1RM por variante) y el Asistente no cambian.
        public Guid? ParentExerciseId { get; private set; }
        public string? VariantLabel { get; private set; }

        public bool IsVariant => ParentExerciseId.HasValue;

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

        // Marca este ejercicio como variante de otro. Un solo nivel: la base no puede ser
        // una variante, y una variante no puede tener variantes propias.
        public void AssignAsVariantOf(Exercise baseExercise, string variantLabel, bool hasOwnVariants = false)
        {
            ArgumentNullException.ThrowIfNull(baseExercise);

            if (baseExercise.Id == Id)
                throw new DomainException("Un ejercicio no puede ser variante de sí mismo.");
            if (baseExercise.IsVariant)
                throw new DomainException("El ejercicio base no puede ser a su vez una variante.");
            if (hasOwnVariants)
                throw new DomainException("Un ejercicio que ya tiene variantes no puede pasar a ser variante de otro.");
            if (string.IsNullOrWhiteSpace(variantLabel))
                throw new DomainException("La variante necesita una etiqueta (por ejemplo, \"Agarre cerrado\").");
            if (variantLabel.Trim().Length > 100)
                throw new DomainException("La etiqueta de la variante no puede superar los 100 caracteres.");

            ParentExerciseId = baseExercise.Id;
            VariantLabel = variantLabel.Trim();
        }
    }
}
