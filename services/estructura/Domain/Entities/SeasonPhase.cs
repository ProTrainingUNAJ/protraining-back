using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class SeasonPhase : BaseEntity
{
    public const int MaxNameLength = 150;

    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public SeasonPhaseType Type { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    private SeasonPhase() { }

    public static SeasonPhase Create(
        Guid categoryId,
        string? name,
        SeasonPhaseType type,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (categoryId == Guid.Empty)
            throw new DomainException("El identificador de la categoría es inválido.");

        var phase = new SeasonPhase
        {
            CategoryId = categoryId
        };

        phase.SetName(name);
        phase.SetType(type);
        phase.SetDates(startDate, endDate);

        return phase;
    }

    public void UpdateDetails(string? name, SeasonPhaseType type)
    {
        SetName(name);
        SetType(type);
    }

    public void UpdateDateRange(DateOnly startDate, DateOnly endDate)
    {
        SetDates(startDate, endDate);
    }

    public bool OverlapsWith(DateOnly start, DateOnly end) =>
        start <= EndDate && end >= StartDate;

    public bool OverlapsWith(SeasonPhase other) =>
        other.CategoryId == CategoryId && OverlapsWith(other.StartDate, other.EndDate);

    private void SetName(string? name)
    {
        var clean = name?.Trim();
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > MaxNameLength)
            throw new DomainException($"El nombre de la fase es obligatorio y debe tener entre 1 y {MaxNameLength} caracteres.");

        Name = clean;
    }

    private void SetType(SeasonPhaseType type)
    {
        if (!Enum.IsDefined(type))
            throw new DomainException("El tipo de fase es inválido. Valores permitidos: Preseason, Competitive, Transition.");

        Type = type;
    }

    private void SetDates(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            throw new DomainException("La fecha de finalización no puede ser anterior a la fecha de inicio.");

        StartDate = startDate;
        EndDate = endDate;
    }
}
