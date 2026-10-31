using Application.DTOs.AthleteRecords;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.AthleteRecord;

// El contrato nombra los orígenes distinto que el dominio: acá se traducen en los dos sentidos.
public static class AthleteRecordMapper
{
    private static readonly Dictionary<RecordSource, string> Origins = new()
    {
        [RecordSource.Tested] = "TestedInSession",
        [RecordSource.Reported] = "Reported",
        [RecordSource.Estimated] = "EstimatedEpley",
        [RecordSource.Epley] = "Epley",
    };

    public static RecordSource ParseOrigin(string? origin)
    {
        foreach (var (source, name) in Origins)
        {
            if (string.Equals(name, origin, StringComparison.OrdinalIgnoreCase))
                return source;
        }

        throw new DomainException("El origen del récord debe ser TestedInSession, Reported, EstimatedEpley o Epley.");
    }

    public static AthleteRecordDto ToDto(Domain.Entities.AthleteRecord record, string exerciseName) => new(
        record.Id,
        record.AthleteId,
        record.ExerciseId,
        exerciseName,
        record.Weight,
        Origins.GetValueOrDefault(record.Source, "Reported"),
        record.Date,
        record.IsActive
    );
}
