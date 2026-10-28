using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class CompetitiveEvent : BaseEntity
{
    public const int MaxOpponentLength = 150;
    public const int MaxNotesLength = 1000;

    public Guid CategoryId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public string Opponent { get; private set; } = string.Empty;
    public CompetitiveEventVenue Venue { get; private set; }
    public CompetitiveEventType Type { get; private set; }
    public string? Notes { get; private set; }

    private CompetitiveEvent() { }

    public static CompetitiveEvent Create(
        Guid categoryId,
        DateOnly date,
        string? opponent,
        CompetitiveEventVenue venue,
        CompetitiveEventType type,
        TimeOnly? startTime = null,
        string? notes = null)
    {
        if (categoryId == Guid.Empty)
            throw new DomainException("El identificador de la categoría es inválido.");

        var ev = new CompetitiveEvent
        {
            CategoryId = categoryId,
            Date = date,
            StartTime = startTime
        };

        ev.SetOpponent(opponent);
        ev.SetVenue(venue);
        ev.SetType(type);
        ev.SetNotes(notes);

        return ev;
    }

    public void UpdateDetails(
        string? opponent,
        CompetitiveEventVenue venue,
        CompetitiveEventType type,
        string? notes)
    {
        SetOpponent(opponent);
        SetVenue(venue);
        SetType(type);
        SetNotes(notes);
    }

    public void Reschedule(DateOnly date, TimeOnly? startTime)
    {
        Date = date;
        StartTime = startTime;
    }

    private void SetOpponent(string? opponent)
    {
        var clean = opponent?.Trim();
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > MaxOpponentLength)
            throw new DomainException($"El nombre del rival es obligatorio y debe tener entre 1 y {MaxOpponentLength} caracteres.");

        Opponent = clean;
    }

    private void SetVenue(CompetitiveEventVenue venue)
    {
        if (!Enum.IsDefined(venue))
            throw new DomainException("La condición de localía es inválida. Valores permitidos: Home, Away, Neutral.");

        Venue = venue;
    }

    private void SetType(CompetitiveEventType type)
    {
        if (!Enum.IsDefined(type))
            throw new DomainException("El tipo de evento es inválido. Valores permitidos: Official, Friendly, Tournament.");

        Type = type;
    }

    private void SetNotes(string? notes)
    {
        var clean = notes?.Trim();
        if (clean is { Length: > MaxNotesLength })
            throw new DomainException($"Las notas del evento no pueden superar los {MaxNotesLength} caracteres.");

        Notes = string.IsNullOrWhiteSpace(clean) ? null : clean;
    }
}
