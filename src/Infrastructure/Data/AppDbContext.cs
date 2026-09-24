using Microsoft.EntityFrameworkCore;
using ProTraining.Application.Common;
using ProTraining.Domain.Entities;

namespace ProTraining.Infrastructure.Data;

public class AppDbContext : DbContext, IAppDbContext
{
    private readonly ICurrentUserService _currentUserService;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserService currentUserService)
        : base(options)
    {
        _currentUserService = currentUserService;
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Athlete> Athletes => Set<Athlete>();
    public DbSet<AthleteCategory> AthleteCategories => Set<AthleteCategory>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<PersonalRecord> PersonalRecords => Set<PersonalRecord>();
    public DbSet<Routine> Routines => Set<Routine>();
    public DbSet<RoutineItem> RoutineItems => Set<RoutineItem>();
    public DbSet<DailyCheckIn> DailyCheckIns => Set<DailyCheckIn>();
    public DbSet<Injury> Injuries => Set<Injury>();
    public DbSet<PreventiveAlert> PreventiveAlerts => Set<PreventiveAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Relación muchos a muchos Atleta - Categoría
        modelBuilder.Entity<AthleteCategory>()
            .HasKey(ac => new { ac.AthleteId, ac.CategoryId });

        modelBuilder.Entity<AthleteCategory>()
            .HasOne(ac => ac.Athlete)
            .WithMany(a => a.AthleteCategories)
            .HasForeignKey(ac => ac.AthleteId);

        modelBuilder.Entity<AthleteCategory>()
            .HasOne(ac => ac.Category)
            .WithMany(c => c.AthleteCategories)
            .HasForeignKey(ac => ac.CategoryId);

        // Filtro global multi-tenant por cuenta del PF (Sección 6 del PDF:
        // "Todas las consultas llevan un filtro global por cuenta del PF,
        // así ningún PF ve datos de otro sin que el desarrollador tenga que acordarse")
        modelBuilder.Entity<Organization>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
        modelBuilder.Entity<Category>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
        modelBuilder.Entity<Athlete>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
        modelBuilder.Entity<AthleteCategory>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
        modelBuilder.Entity<PersonalRecord>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
        modelBuilder.Entity<Routine>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
        modelBuilder.Entity<DailyCheckIn>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
        modelBuilder.Entity<Injury>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
        modelBuilder.Entity<PreventiveAlert>().HasQueryFilter(e => !_currentUserService.AccountId.HasValue || e.AccountId == _currentUserService.AccountId.Value);
    }
}
