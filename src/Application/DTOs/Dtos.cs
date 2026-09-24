using ProTraining.Domain.Entities;

namespace ProTraining.Application.DTOs;

// Autenticación
public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string FullName, string Email, string Role, Guid AccountId, Guid? AthleteId);
public record RegisterRequest(string FullName, string Email, string Password);

// Estructura
public record OrganizationDto(Guid Id, string Name, string? Description, int CategoriesCount);
public record CreateOrganizationRequest(string Name, string? Description);

public record CategoryDto(Guid Id, Guid OrganizationId, string Name, int AthletesCount);
public record CreateCategoryRequest(Guid OrganizationId, string Name);

public record AthleteDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string? MedicalConditions,
    string? EmergencyContact,
    List<string> CategoryNames,
    int ActiveInjuriesCount
);

public record CreateAthleteRequest(
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    List<Guid> CategoryIds,
    string? MedicalConditions,
    string? EmergencyContact
);

// Entrenamiento
public record ExerciseDto(Guid Id, string Name, string MuscleGroup, string? Description);

public record PersonalRecordDto(Guid Id, Guid ExerciseId, string ExerciseName, decimal WeightOneRepMax, DateTime DateAchieved);
public record SaveRecordRequest(Guid AthleteId, Guid ExerciseId, decimal WeightOneRepMax);

public record RoutineItemRequest(Guid ExerciseId, int Sets, int Reps, decimal Percentage, int Order);
public record CreateRoutineRequest(string Title, Guid? CategoryId, Guid? AthleteId, DayOfWeek DayOfWeek, List<RoutineItemRequest> Items);

public record TodayRoutineDto(
    Guid AthleteId,
    string AthleteName,
    DateTime Date,
    List<TodayRoutineItemDto> Exercises,
    bool HasActiveInjuries
);

public record TodayRoutineItemDto(
    Guid ExerciseId,
    string ExerciseName,
    string MuscleGroup,
    int Sets,
    int Reps,
    decimal Percentage,
    decimal PrescribedWeightKg, // Calculado sobre el 1RM vigente con redondeo a 2.5 kg
    decimal CurrentOneRepMax
);

public record LogExecutionRequest(
    Guid AthleteId,
    Guid ExerciseId,
    decimal WeightUsed,
    int RepsPerformed
);

// Seguimiento
public record DailyCheckInRequest(
    Guid AthleteId,
    int EffortRate,
    decimal SleepHours,
    int MoodRate,
    string PainZones
);

public record DailyCheckInDto(
    Guid Id,
    Guid AthleteId,
    DateTime Date,
    int EffortRate,
    decimal SleepHours,
    int MoodRate,
    string PainZones
);

public record CreateInjuryRequest(Guid AthleteId, string BodyZone, string Notes);
public record UpdateInjuryStatusRequest(InjuryStatus NewStatus, string TransitionNote);
public record InjuryDto(Guid Id, Guid AthleteId, string BodyZone, string Notes, InjuryStatus Status, DateTime CreatedAt);

// Avisos
public record PreventiveAlertDto(
    Guid Id,
    Guid AthleteId,
    string AthleteName,
    DateTime DateGenerated,
    string Severity,
    string Reason,
    string Suggestion,
    bool IsReviewed
);
