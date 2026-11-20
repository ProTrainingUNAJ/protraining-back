using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities
{
    public class AthleteVariant : BaseEntity
    {
        public Guid AthleteId { get; private set; }
        public Guid OriginalExerciseId { get; private set; }
        public Guid SubstituteExerciseId { get; private set; }
        public bool IsApproved { get; private set; }
        public bool IsActive { get; private set; }
        public DateOnly ValidFrom { get; private set; }
        public DateOnly? ValidTo { get; private set; }
        public string? Note { get; private set; }

        private AthleteVariant() { }

        public static AthleteVariant Create(
            Guid athleteId,
            Guid originalExerciseId,
            Guid substituteExerciseId,
            bool isApproved,
            bool isActive,
            DateOnly validFrom,
            DateOnly? validTo = null,
            string? note = null
            )
        {
            if (athleteId == Guid.Empty)
                throw new DomainException("El identificador del atleta es obligatorio.");

            if (originalExerciseId == Guid.Empty)
                throw new DomainException("El ejercicio original es obligatorio.");
            if (substituteExerciseId == Guid.Empty)
                throw new DomainException("El ejercicio sustituto es obligatorio.");

            if (originalExerciseId == substituteExerciseId)
                throw new DomainException("El ejercicio sustituto debe ser distinto al ejercicio original.");

            if (validFrom == default)
                throw new DomainException("La fecha de inicio de vigencia es obligatoria.");

            if (validTo.HasValue && validTo.Value < validFrom)
                throw new DomainException("La fecha de fin de vigencia no puede ser anterior a la fecha de inicio.");

            return new AthleteVariant
            {
                AthleteId = athleteId,
                OriginalExerciseId = originalExerciseId,
                SubstituteExerciseId = substituteExerciseId,
                IsApproved = isApproved,
                IsActive = isActive,
                ValidFrom = validFrom,
                ValidTo = validTo,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            };
        }
    }
}
