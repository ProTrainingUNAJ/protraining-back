using System.Globalization;
using Application.DTOs.Categories;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Common;
using Domain.Exceptions;

namespace Application.UseCases.Categories.Events.ListCategoryEvents;

public sealed record ListCategoryEventsQuery(
    Guid CategoryId,
    string? From = null,
    string? To = null);

public sealed class ListCategoryEventsQueryHandler(
    ICategoryAccessValidator accessValidator,
    ICompetitiveEventRepository eventRepository,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<CompetitiveEventDto>> HandleAsync(
        ListCategoryEventsQuery query,
        CancellationToken cancellationToken = default)
    {
        await accessValidator.ValidateReadAccessAsync(query.CategoryId, cancellationToken);

        DateOnly fromDate;
        if (!string.IsNullOrWhiteSpace(query.From))
        {
            if (!DateOnly.TryParseExact(query.From.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDate))
            {
                throw new DomainException("El formato de fecha 'from' es inválido. Debe ser YYYY-MM-DD.");
            }
        }
        else
        {
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var argentinaNow = ArgentinaTime.FromUtc(utcNow);
            fromDate = DateOnly.FromDateTime(argentinaNow);
        }

        DateOnly toDate;
        if (!string.IsNullOrWhiteSpace(query.To))
        {
            if (!DateOnly.TryParseExact(query.To.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out toDate))
            {
                throw new DomainException("El formato de fecha 'to' es inválido. Debe ser YYYY-MM-DD.");
            }
        }
        else
        {
            toDate = fromDate.AddDays(14);
        }

        if (toDate < fromDate)
        {
            throw new DomainException("La fecha 'to' no puede ser anterior a la fecha 'from'.");
        }

        var events = await eventRepository.ListByCategoryAndDateRangeAsync(
            query.CategoryId,
            fromDate,
            toDate,
            cancellationToken);

        return events.Select(CategoryMappings.ToDto).ToList();
    }
}
