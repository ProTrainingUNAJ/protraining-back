using Application.DTOs.Categories;
using Domain.Entities;

namespace Application.Mappings;

public static class CategoryMappings
{
    public static CategoryDto ToDto(Category category) =>
        new(category.Id, category.OrganizationId, category.Name, category.Description);

    public static CompetitiveEventDto ToDto(CompetitiveEvent ev) =>
        new(ev.Id, ev.Date, ev.StartTime, ev.Opponent, ev.Venue, ev.Type, ev.Notes);

    public static SeasonPhaseDto ToDto(SeasonPhase phase) =>
        new(phase.Id, phase.Name, phase.Type, phase.StartDate, phase.EndDate);
}
