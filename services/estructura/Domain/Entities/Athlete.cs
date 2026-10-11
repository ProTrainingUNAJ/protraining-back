using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class Athlete : BaseEntity
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public bool IsMinor { get; private set; }
    public string? EmergencyContact { get; private set; }
    public string? MedicalConditions { get; private set; }
    public bool Active { get; private set; } = true;
    public Guid? AccountId { get; private set; }

    public DigitalConsent? DigitalConsent { get; private set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool HasSignedConsent => DigitalConsent?.IsSigned == true;
    public bool AllowsHabitsRecommendations => DigitalConsent?.IsSigned == true && DigitalConsent.AllowsHabitsRecommendations;

    private Athlete() { }

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
        string? medicalConditions = null)
    {
        var athlete = new Athlete();
        athlete.ValidateAndAssignPersonalData(firstName, lastName, email, phone, emergencyContact);
        athlete.IsMinor = true;
        athlete.MedicalConditions = medicalConditions?.Trim();

        athlete.DigitalConsent = DigitalConsent.RegisterForMinor(
            athlete.Id,
            guardianName,
            guardianEmail,
            guardianPhone,
            allowsPhysicalTraining,
            allowsHabitsRecommendations);

        return athlete;
    }

    public static Athlete CreateAdult(
        string? firstName,
        string? lastName,
        string? email,
        string? phone,
        bool signsInitialConsent = false,
        string? emergencyContact = null,
        string? medicalConditions = null)
    {
        var athlete = new Athlete();
        athlete.ValidateAndAssignPersonalData(firstName, lastName, email, phone, emergencyContact);
        athlete.IsMinor = false;
        athlete.MedicalConditions = medicalConditions?.Trim();

        if (signsInitialConsent)
        {
            athlete.DigitalConsent = DigitalConsent.RegisterForAdult(athlete.Id);
        }

        return athlete;
    }

    public void RegisterGuardianConsent(
        string? guardianName,
        string? guardianEmail,
        string? guardianPhone,
        bool allowsPhysicalTraining,
        bool allowsHabitsRecommendations)
    {
        if (!IsMinor)
            throw new DomainException("Solo los atletas menores de edad requieren consentimiento de un tutor.");

        DigitalConsent = DigitalConsent.RegisterForMinor(
            Id,
            guardianName,
            guardianEmail,
            guardianPhone,
            allowsPhysicalTraining,
            allowsHabitsRecommendations);
    }

    public void RegisterAdultConsent(
        bool allowsPhysicalTraining = true,
        bool allowsHabitsRecommendations = true)
    {
        if (IsMinor)
            throw new DomainException("Los atletas menores de edad deben ser autorizados por un tutor legal.");

        DigitalConsent = DigitalConsent.RegisterForAdult(
            Id,
            allowsPhysicalTraining,
            allowsHabitsRecommendations);
    }

    public void UpdateHabitsPermission(bool allowsHabitsRecommendations)
    {
        if (DigitalConsent == null || !DigitalConsent.IsSigned)
            throw new DomainException("No existe un consentimiento firmado para modificar los permisos.");

        DigitalConsent.UpdateHabitsPermission(allowsHabitsRecommendations);
    }

    public void LinkAccount(Guid accountId)
    {
        if (accountId == Guid.Empty)
            throw new DomainException("El identificador de la cuenta es inválido.");

        AccountId = accountId;
    }

    public void UpdateContactInfo(string? phone, string? emergencyContact)
    {
        var cleanPhone = phone?.Trim();
        if (string.IsNullOrWhiteSpace(cleanPhone) || cleanPhone.Length > 50)
            throw new DomainException("El teléfono no puede estar vacío ni superar los 50 caracteres.");

        Phone = cleanPhone;
        EmergencyContact = emergencyContact?.Trim();
    }

    public void SetActive(bool active) => Active = active;

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

        var cleanEmail = email?.Trim();
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
