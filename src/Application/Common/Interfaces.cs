using Microsoft.EntityFrameworkCore;
using ProTraining.Domain.Entities;

namespace ProTraining.Application.Common;

public interface ICurrentUserService
{
    Guid? AccountId { get; }
    Guid? UserId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}

public interface IAppDbContext
{
    DbSet<Account> Accounts { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<Category> Categories { get; }
    DbSet<Athlete> Athletes { get; }
    DbSet<AthleteCategory> AthleteCategories { get; }
    DbSet<Exercise> Exercises { get; }
    DbSet<PersonalRecord> PersonalRecords { get; }
    DbSet<Routine> Routines { get; }
    DbSet<RoutineItem> RoutineItems { get; }
    DbSet<DailyCheckIn> DailyCheckIns { get; }
    DbSet<Injury> Injuries { get; }
    DbSet<PreventiveAlert> PreventiveAlerts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class AnalysisResult
{
    public string Severity { get; set; } = "Media";
    public string Reason { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public bool RequiresAlert { get; set; }
}

public interface IProveedorDeAnalisis
{
    Task<AnalysisResult?> AnalizarSemanaAtletaAsync(Athlete atleta, List<DailyCheckIn> checkIns, List<Injury> lesionesActivas, CancellationToken ct = default);
}
