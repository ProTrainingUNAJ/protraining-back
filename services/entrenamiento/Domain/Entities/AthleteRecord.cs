using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public class AthleteRecord : BaseEntity
{
    public Guid AthleteId { get; private set; }
    public Guid ExerciseId { get; private set; }
    public decimal Weight { get; private set; }
    public DateTime Date { get; private set; }
    public bool IsActive { get; private set; }
    public RecordSource Source {  get; private set; }
    public Exercise? Exercise { get; private set; }
    private AthleteRecord() { }


    public static AthleteRecord Create(Guid athleteId, Guid exerciseId, decimal weight, RecordSource source, DateTime date)
    {
        if (weight <= 0)
        {
            throw new DomainException("El peso del récord debe ser mayor a 0 kg.");
        }

        if (athleteId == Guid.Empty || exerciseId == Guid.Empty)
        {
            throw new DomainException("El atleta y el ejercicio son obligatorios.");
        }

        return new AthleteRecord
        {
            AthleteId = athleteId,
            ExerciseId = exerciseId,
            Weight = weight,
            Source = source,
            Date = date,
            IsActive = true
        };
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}


public enum RecordSource
{
    Tested,
    Reported,
    Estimated,
    NoData
}