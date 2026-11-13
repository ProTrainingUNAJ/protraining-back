namespace Application.DTOs.DailyLoad;

// Día de la semana del atleta con su estado de carga (KAN-446): marcado si registró
// la carga del día en esa fecha.
public sealed record AthleteWeekDayDto(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    bool Completed);

// Semana del atleta con los días completados (KAN-446): permite pintar la tira de
// la semana marcando qué días cargó el atleta.
public sealed record AthleteWeekDto(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    IReadOnlyList<DateOnly> CompletedDays,
    IReadOnlyList<AthleteWeekDayDto> Days);
