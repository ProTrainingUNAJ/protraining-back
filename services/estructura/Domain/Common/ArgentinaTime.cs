using System;

namespace Domain.Common;

// El día del check-in se calcula en hora argentina, nunca en UTC (KAN-329).
public static class ArgentinaTime
{
    // Mismo criterio en Windows y Linux: la convención del repo es la zona
    // America/Argentina/Buenos_Aires (README, sección "Fechas: solo Argentina").
    private static readonly TimeZoneInfo ZonaArgentina = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows()
            ? "Argentina Standard Time"
            : "America/Argentina/Buenos_Aires");

    // Hora argentina de un instante UTC (puro: testeable con instantes fijos).
    public static DateTime FromUtc(DateTime utcNow) =>
        TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), ZonaArgentina);

    public static DateTime Now => FromUtc(DateTime.UtcNow);

    public static DateOnly Today => DateOnly.FromDateTime(Now);
}
