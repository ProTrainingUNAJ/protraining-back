using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class AthleteCategory : BaseEntity
{
    public Guid AthleteId { get; private set; }
    public Guid CategoryId { get; private set; }

    private AthleteCategory() { }

    public static AthleteCategory Assign(Guid athleteId, Guid categoryId)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        if (categoryId == Guid.Empty)
            throw new DomainException("El identificador de la categoría es inválido.");

        return new AthleteCategory { AthleteId = athleteId, CategoryId = categoryId };
    }
}
