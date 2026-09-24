using Application.DTOs;
using Domain.Entities;

namespace Application.Mappings;

public static class AthleteMappings
{
    public static AthleteDto ToDto(Athlete athlete) =>
        new(athlete.Id, athlete.FullName, athlete.Category, athlete.Record1RmKg, athlete.Active);
}
