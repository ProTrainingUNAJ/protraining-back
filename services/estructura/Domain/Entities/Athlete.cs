using Domain.Common;
using Domain.Exceptions;
using Domain.Services;

namespace Domain.Entities;

public sealed class Athlete : BaseEntity
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? TelegramUsername { get; private set; }
    public decimal? WeightKg { get; private set; }
    public AthleticGoal? AthleticGoal { get; private set; }
    public DateTime? PhysicalProfileUpdatedAt { get; private set; }
    public bool IsMinor { get; private set; }
    public string? EmergencyContact { get; private set; }
    public bool Active { get; private set; } = true;
    public Guid? AccountId { get; private set; }
    public bool HasPhoto { get; private set; }
    public DateTime? PhotoUpdatedAt { get; private set; }

    public DigitalConsent? DigitalConsent { get; private set; }
    public MedicalRecord? MedicalRecord { get; private set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool HasSignedConsent => DigitalConsent?.IsValidForAthlete(IsMinor) == true;
    public bool AllowsHabitsRecommendations => HasSignedConsent && DigitalConsent!.AllowsHabitsRecommendations;
    public bool AllowsProfilePhoto => !IsMinor || (HasSignedConsent && (DigitalConsent?.AllowsProfilePhoto ?? false));

    private Athlete() { }

    // Calcula la edad en años cumplidos respecto a una fecha de referencia (ej. hora de Argentina).
    public static int CalculateAgeOn(DateOnly birthDate, DateOnly referenceDate)
    {
        if (birthDate > referenceDate)
            throw new DomainException("La fecha de nacimiento no puede ser futura.");

        var age = referenceDate.Year - birthDate.Year;
        if (birthDate > referenceDate.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    // Determina si una persona es menor de 18 años a una fecha de referencia dada (KAN-401).
    public static bool IsMinorOn(DateOnly birthDate, DateOnly referenceDate) =>
        CalculateAgeOn(birthDate, referenceDate) < 18;
    public static Athlete CreateMinor(
        string? firstName,
        string? lastName,
        string? email,
        string? phone,
        string? guardianName,
        string? guardianEmail,
        string? guardianPhone,
        bool allowsPhysicalTraining,
        bool allowsHabitsRecommendations,
        string? emergencyContact = null,
        bool allowsProfilePhoto = false,
        string? telegramUsername = null)
    {
        var athlete = new Athlete();
        athlete.ValidateAndAssignPersonalData(firstName, lastName, email, phone, emergencyContact);
        athlete.UpdateTelegramUsername(telegramUsername);
        athlete.IsMinor = true;

        athlete.DigitalConsent = DigitalConsent.RegisterForMinor(
            athlete.Id,
            guardianName,
            guardianEmail,
            guardianPhone,
            allowsPhysicalTraining,
            allowsHabitsRecommendations,
            allowsProfilePhoto);

        return athlete;
    }

    // Permite dar de alta un atleta menor cuyo tutor legal aún no ha firmado el consentimiento digital.
    public static Athlete CreateMinorPendingConsent(
        string? firstName,
        string? lastName,
        string? email,
        string? phone,
        string? emergencyContact = null,
        string? telegramUsername = null)
    {
        var athlete = new Athlete();
        athlete.ValidateAndAssignPersonalData(firstName, lastName, email, phone, emergencyContact);
        athlete.UpdateTelegramUsername(telegramUsername);
        athlete.IsMinor = true;
        return athlete;
    }

    public static Athlete CreateAdult(
        string? firstName,
        string? lastName,
        string? email,
        string? phone,
        bool signsInitialConsent = false,
        string? emergencyContact = null,
        bool allowsProfilePhoto = false,
        string? telegramUsername = null)
    {
        var athlete = new Athlete();
        athlete.ValidateAndAssignPersonalData(firstName, lastName, email, phone, emergencyContact);
        athlete.UpdateTelegramUsername(telegramUsername);
        athlete.IsMinor = false;

        if (signsInitialConsent)
        {
            athlete.DigitalConsent = DigitalConsent.RegisterForAdult(
                athlete.Id,
                allowsPhysicalTraining: true,
                allowsHabitsRecommendations: false,
                allowsProfilePhoto: allowsProfilePhoto);
        }

        return athlete;
    }

    public void RegisterGuardianConsent(
        string? guardianName,
        string? guardianEmail,
        string? guardianPhone,
        bool allowsPhysicalTraining,
        bool allowsHabitsRecommendations,
        bool allowsProfilePhoto = false)
    {
        if (!IsMinor)
            throw new DomainException("Solo los atletas menores de edad requieren consentimiento de un tutor.");

        if (DigitalConsent != null && DigitalConsent.IsSigned)
            throw new DomainException("El consentimiento digital ya fue firmado y su registro es inmutable.");

        DigitalConsent = DigitalConsent.RegisterForMinor(
            Id,
            guardianName,
            guardianEmail,
            guardianPhone,
            allowsPhysicalTraining,
            allowsHabitsRecommendations,
            allowsProfilePhoto);
    }

    public void RegisterAdultConsent(
        bool allowsPhysicalTraining = true,
        bool allowsHabitsRecommendations = false,
        bool allowsProfilePhoto = true)
    {
        if (IsMinor)
            throw new DomainException("Los atletas menores de edad deben ser autorizados por un tutor legal.");

        if (DigitalConsent != null && DigitalConsent.IsSigned)
            throw new DomainException("El consentimiento digital ya fue firmado y su registro es inmutable.");

        DigitalConsent = DigitalConsent.RegisterForAdult(
            Id,
            allowsPhysicalTraining,
            allowsHabitsRecommendations,
            allowsProfilePhoto);
    }

    public void UpdateHabitsPermission(bool allowsHabitsRecommendations)
    {
        if (DigitalConsent == null || !DigitalConsent.IsSigned)
            throw new DomainException("No existe un consentimiento firmado para modificar los permisos.");

        DigitalConsent.UpdateHabitsPermission(allowsHabitsRecommendations);
    }

    public void UpdateProfilePhotoPermission(bool allowsProfilePhoto)
    {
        if (DigitalConsent == null || !DigitalConsent.IsSigned)
            throw new DomainException("No existe un consentimiento firmado para modificar los permisos.");

        DigitalConsent.UpdateProfilePhotoPermission(allowsProfilePhoto);
    }

    public void SetPhotoUpdated(DateTime timestamp)
    {
        HasPhoto = true;
        PhotoUpdatedAt = timestamp;
    }

    public void RemovePhoto()
    {
        HasPhoto = false;
        PhotoUpdatedAt = null;
    }

    // Lee la ficha médica del atleta exigiendo que el consentimiento digital esté firmado (o el del tutor si es menor).
    public MedicalRecord? ReadMedicalRecord()
    {
        EnsureConsentSignedForMedicalRecord();
        return MedicalRecord;
    }

    // Completa o actualiza la ficha médica del atleta exigiendo consentimiento digital firmado (del atleta o del tutor si es menor).
    public MedicalRecord CompleteOrUpdateMedicalRecord(
        string? medicalHistory,
        string? surgeries,
        string? pathologies,
        bool hasPhysicalFitnessCertificate,
        DateOnly? physicalFitnessIssuedAt = null,
        DateOnly? physicalFitnessExpiresAt = null,
        string? physicalFitnessNotes = null)
    {
        EnsureConsentSignedForMedicalRecord();

        if (MedicalRecord is null)
        {
            MedicalRecord = MedicalRecord.Create(
                Id,
                medicalHistory,
                surgeries,
                pathologies,
                hasPhysicalFitnessCertificate,
                physicalFitnessIssuedAt,
                physicalFitnessExpiresAt,
                physicalFitnessNotes);
        }
        else
        {
            MedicalRecord.Update(
                medicalHistory,
                surgeries,
                pathologies,
                hasPhysicalFitnessCertificate,
                physicalFitnessIssuedAt,
                physicalFitnessExpiresAt,
                physicalFitnessNotes);
        }

        return MedicalRecord;
    }

    public void LinkAccount(Guid accountId)
    {
        if (accountId == Guid.Empty)
            throw new DomainException("El identificador de la cuenta es inválido.");

        AccountId = accountId;
    }

    public void UpdateContactInfo(string? phone, string? emergencyContact, string? telegramUsername = null)
    {
        var cleanPhone = phone?.Trim();
        if (string.IsNullOrWhiteSpace(cleanPhone) || cleanPhone.Length > 50)
            throw new DomainException("El teléfono no puede estar vacío ni superar los 50 caracteres.");

        Phone = cleanPhone;
        EmergencyContact = emergencyContact?.Trim();

        if (telegramUsername is not null)
        {
            UpdateTelegramUsername(telegramUsername);
        }
    }

    public void UpdateTelegramUsername(string? telegramUsername)
    {
        var (isValid, normalized, errorMessage) = TelegramUsernameNormalizer.ValidateAndNormalize(telegramUsername);
        if (!isValid)
        {
            throw new DomainException(errorMessage ?? "El usuario de Telegram no es válido.");
        }

        TelegramUsername = normalized;
    }

    /// <summary>
    /// Actualiza el perfil físico usado por las recomendaciones de hábitos.
    /// null en peso/objetivo significa "sin dato": nunca se inventa un valor por defecto.
    /// </summary>
    public void UpdatePhysicalProfile(decimal? weightKg, string? athleticGoalLabel)
    {
        // Validar todo antes de mutar: un objetivo inválido no debe dejar el peso a medias.
        if (weightKg.HasValue && (weightKg.Value <= 0 || weightKg.Value > 400))
            throw new DomainException("El peso corporal debe estar entre 0 y 400 kg.");

        if (!AthleticGoalLabels.TryParse(athleticGoalLabel, out var goal))
            throw new DomainException(
                $"El objetivo deportivo no es válido. Valores permitidos: {AthleticGoalLabels.ValidValues}.");

        WeightKg = weightKg.HasValue
            ? decimal.Round(weightKg.Value, 2, MidpointRounding.AwayFromZero)
            : null;
        AthleticGoal = goal;
        PhysicalProfileUpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active) => Active = active;

    public void UpdateEmail(string newEmail)
    {
        if (string.IsNullOrWhiteSpace(newEmail))
            throw new DomainException("El correo electrónico no puede estar vacío.");

        var cleanEmail = newEmail.Trim().ToLowerInvariant();
        if (!cleanEmail.Contains('@') || !cleanEmail.Contains('.'))
            throw new DomainException("El formato del correo electrónico no es válido.");

        Email = cleanEmail;
    }

    private void EnsureConsentSignedForMedicalRecord()
    {
        if (!HasSignedConsent)
        {
            throw new DomainException(IsMinor
                ? "La ficha médica no puede ser leída ni completada porque el tutor legal aún no ha firmado el consentimiento digital."
                : "La ficha médica no puede ser leída ni completada porque el atleta aún no ha firmado el consentimiento digital.");
        }
    }

    private void ValidateAndAssignPersonalData(
        string? firstName,
        string? lastName,
        string? email,
        string? phone,
        string? emergencyContact)
    {
        var cleanFirst = firstName?.Trim();
        if (string.IsNullOrWhiteSpace(cleanFirst) || cleanFirst.Length > 100)
            throw new DomainException("El nombre es obligatorio y debe tener entre 1 y 100 caracteres.");

        var cleanLast = lastName?.Trim();
        if (string.IsNullOrWhiteSpace(cleanLast) || cleanLast.Length > 100)
            throw new DomainException("El apellido es obligatorio y debe tener entre 1 y 100 caracteres.");

        // En minúsculas, igual que Identidad y UpdateEmail: así "Lucas@X.com" y "lucas@x.com" son el mismo
        // correo para detectar duplicados (KAN-660).
        var cleanEmail = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(cleanEmail) || cleanEmail.Length > 150 || !cleanEmail.Contains('@'))
            throw new DomainException("El correo electrónico es obligatorio y debe tener un formato válido.");

        var cleanPhone = phone?.Trim();
        if (string.IsNullOrWhiteSpace(cleanPhone) || cleanPhone.Length > 50)
            throw new DomainException("El teléfono es obligatorio y no puede superar los 50 caracteres.");

        FirstName = cleanFirst;
        LastName = cleanLast;
        Email = cleanEmail;
        Phone = cleanPhone;
        EmergencyContact = emergencyContact?.Trim();
        Active = true;
    }
}
