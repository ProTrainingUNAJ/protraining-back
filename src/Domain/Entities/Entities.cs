using ProTraining.Common;

namespace ProTraining.Domain.Entities;

public enum UserRole
{
    PF = 1,
    Athlete = 2
}

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.PF;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; } // Tenant del PF
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Category> Categories { get; set; } = [];
}

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; } // Tenant del PF
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization? Organization { get; set; }
    public List<AthleteCategory> AthleteCategories { get; set; } = [];
}

public class Athlete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; } // Tenant del PF
    public Guid? UserId { get; set; }   // Cuenta de usuario opcional para el atleta
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    
    // Ficha médica
    public string? MedicalConditions { get; set; }
    public string? EmergencyContact { get; set; }
    public bool ConsentGiven { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<AthleteCategory> AthleteCategories { get; set; } = [];
    public List<PersonalRecord> Records { get; set; } = [];
    public List<DailyCheckIn> CheckIns { get; set; } = [];
    public List<Injury> Injuries { get; set; } = [];
}

public class AthleteCategory
{
    public Guid AthleteId { get; set; }
    public Guid CategoryId { get; set; }
    public Guid AccountId { get; set; }

    public Athlete? Athlete { get; set; }
    public Category? Category { get; set; }
}

public class Exercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string MuscleGroup { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class PersonalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid AthleteId { get; set; }
    public Guid ExerciseId { get; set; }
    public decimal WeightOneRepMax { get; set; }
    public DateTime DateAchieved { get; set; } = DateTime.UtcNow;
    public bool IsCurrent { get; set; } = true;

    public Athlete? Athlete { get; set; }
    public Exercise? Exercise { get; set; }
}

public class Routine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; } // Asignada a categoría
    public Guid? AthleteId { get; set; }  // O asignada directamente a un atleta individual
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Category? Category { get; set; }
    public Athlete? Athlete { get; set; }
    public List<RoutineItem> Items { get; set; } = [];
}

public class RoutineItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoutineId { get; set; }
    public Guid ExerciseId { get; set; }
    public int Sets { get; set; }
    public int Reps { get; set; }
    public decimal Percentage { get; set; } // % de 1RM
    public int Order { get; set; }

    public Routine? Routine { get; set; }
    public Exercise? Exercise { get; set; }
}

public class DailyCheckIn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid AthleteId { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public int EffortRate { get; set; } // 1 a 10
    public decimal SleepHours { get; set; }
    public int MoodRate { get; set; } // 1 a 5
    public string PainZones { get; set; } = string.Empty; // Zonas de molestia separadas por coma
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Athlete? Athlete { get; set; }
}

public enum InjuryStatus
{
    Active = 1,
    InRecovery = 2,
    Cleared = 3
}

public class Injury
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid AthleteId { get; set; }
    public string BodyZone { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public InjuryStatus Status { get; private set; } = InjuryStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Athlete? Athlete { get; set; }

    public void TransitionTo(InjuryStatus newStatus, string transitionNote)
    {
        if (Status == InjuryStatus.Active && newStatus == InjuryStatus.Cleared)
        {
            throw new DomainException("Una lesión activa no puede pasar directamente a Alta. Debe pasar primero por estado 'En recuperación'.");
        }

        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
        Notes += $" | [{DateTime.UtcNow:yyyy-MM-dd}] Estado a {newStatus}: {transitionNote}";
    }
}

public class PreventiveAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid AthleteId { get; set; }
    public DateTime DateGenerated { get; set; } = DateTime.UtcNow;
    public string Severity { get; set; } = "Media"; // Baja, Media, Alta
    public string Reason { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public bool IsReviewed { get; set; } = false;

    public Athlete? Athlete { get; set; }
}
