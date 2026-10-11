using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public sealed class DigitalConsent : EntidadBase
{
    public Guid AthleteId { get; private set; }
    public bool IsSigned { get; private set; }
    public DateTime ConsentDate { get; private set; }
    public bool IsMinor { get; private set; }
    public string? GuardianName { get; private set; }
    public string? GuardianEmail { get; private set; }
    public string? GuardianPhone { get; private set; }
    public bool AllowsPhysicalTraining { get; private set; }
    public bool AllowsHabitsRecommendations { get; private set; }

    private DigitalConsent() { }

    public static DigitalConsent RegisterForMinor(
        Guid athleteId,
        string? guardianName,
        string? guardianEmail,
        string? guardianPhone,
        bool allowsPhysicalTraining,
        bool allowsHabitsRecommendations)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var name = guardianName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
            throw new DomainException("El nombre del tutor es obligatorio y no puede superar los 150 caracteres.");

        var email = guardianEmail?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 150 || !email.Contains('@'))
            throw new DomainException("El correo electrónico del tutor es obligatorio y debe tener un formato válido.");

        var phone = guardianPhone?.Trim();
        if (string.IsNullOrWhiteSpace(phone) || phone.Length > 50)
            throw new DomainException("El teléfono del tutor es obligatorio y no puede superar los 50 caracteres.");

        if (!allowsPhysicalTraining)
            throw new DomainException("El tutor legal debe autorizar obligatoriamente la preparación física.");

        return new DigitalConsent
        {
            AthleteId = athleteId,
            IsSigned = true,
            ConsentDate = DateTime.UtcNow,
            IsMinor = true,
            GuardianName = name,
            GuardianEmail = email,
            GuardianPhone = phone,
            AllowsPhysicalTraining = true,
            AllowsHabitsRecommendations = allowsHabitsRecommendations
        };
    }

    public static DigitalConsent RegisterForAdult(
        Guid athleteId,
        bool allowsPhysicalTraining = true,
        bool allowsHabitsRecommendations = true)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        if (!allowsPhysicalTraining)
            throw new DomainException("El consentimiento debe autorizar la preparación física.");

        return new DigitalConsent
        {
            AthleteId = athleteId,
            IsSigned = true,
            ConsentDate = DateTime.UtcNow,
            IsMinor = false,
            AllowsPhysicalTraining = true,
            AllowsHabitsRecommendations = allowsHabitsRecommendations
        };
    }

    public void UpdateHabitsPermission(bool allowsHabitsRecommendations)
    {
        AllowsHabitsRecommendations = allowsHabitsRecommendations;
        ConsentDate = DateTime.UtcNow;
    }
}
