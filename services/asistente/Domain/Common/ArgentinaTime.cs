namespace Domain.Common;

public static class ArgentinaTime
{
    private const string TimeZoneId = "America/Argentina/Buenos_Aires";

    public static DateTime Now => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, TimeZoneId);

    public static DateOnly Today => DateOnly.FromDateTime(Now);

    public static DayOfWeek DayOfWeek => Now.DayOfWeek;
}
