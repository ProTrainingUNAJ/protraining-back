using Domain.Entities;
using Domain.Exceptions;

namespace Seguimiento.Tests;

// KAN-420: máquina de estados estricta Active → InRecovery → MedicallyCleared.
// Toda violación de regla sale del dominio como DomainException con mensaje en español.
public class AthleteInjuryTransitionTests
{
    private static readonly Guid AthleteId = Guid.NewGuid();
    private static readonly Guid ChangedBy = Guid.NewGuid();
    private static readonly DateTime ChangedAt = new(2026, 10, 27, 15, 30, 0, DateTimeKind.Utc);

    private static AthleteInjury CrearActiva() =>
        AthleteInjury.Create(
            AthleteId,
            "isquiotibiales",
            "Gradación 3 tras sprint",
            new DateTime(2026, 10, 20),
            new DateTime(2026, 11, 10));

    private static AthleteInjury CrearEnRecuperacion()
    {
        var injury = CrearActiva();
        injury.TransitionTo(InjuryStatus.InRecovery, "Inicio de readaptación.", ChangedBy, ChangedAt);
        return injury;
    }

    [Fact]
    public void TransitionTo_FromActiveToInRecovery_WithNote_ChangesStatusAndReturnsHistoryRow()
    {
        var injury = CrearActiva();

        var row = injury.TransitionTo(InjuryStatus.InRecovery, "  Inicio de readaptación.  ", ChangedBy, ChangedAt);

        Assert.Equal(InjuryStatus.InRecovery, injury.Status);
        Assert.Null(injury.ClearedAt);

        Assert.Equal(injury.Id, row.InjuryId);
        Assert.Equal(InjuryStatus.Active, row.FromStatus);
        Assert.Equal(InjuryStatus.InRecovery, row.ToStatus);
        Assert.Equal("Inicio de readaptación.", row.Note);
        Assert.Equal(ChangedAt, row.ChangedAt);
        Assert.Equal(ChangedBy, row.ChangedBy);
    }

    [Fact]
    public void TransitionTo_FromInRecoveryToMedicallyCleared_LiberatesAthleteAndReturnsHistoryRow()
    {
        var injury = CrearEnRecuperacion();

        var row = injury.TransitionTo(InjuryStatus.MedicallyCleared, "Alta confirmada tras kinesiología.", ChangedBy, ChangedAt);

        Assert.Equal(InjuryStatus.MedicallyCleared, injury.Status);

        // Se libera al atleta: queda asentado cuándo ocurrió el alta médica.
        Assert.Equal(ChangedAt, injury.ClearedAt);

        Assert.Equal(InjuryStatus.InRecovery, row.FromStatus);
        Assert.Equal(InjuryStatus.MedicallyCleared, row.ToStatus);
        Assert.Equal(ChangedBy, row.ChangedBy);
    }

    // CA1: prohibido dar de alta una lesión activa sin pasar antes por En recuperación.
    [Fact]
    public void TransitionTo_DirectlyToMedicallyCleared_ThrowsDomainExceptionRequiringRecovery()
    {
        var injury = CrearActiva();

        var exception = Assert.Throws<DomainException>(
            () => injury.TransitionTo(InjuryStatus.MedicallyCleared, "Alta directa.", ChangedBy, ChangedAt));

        Assert.Equal(
            "Una lesión activa no puede pasar directamente a 'Alta médica'. Debe pasar primero por 'En recuperación'.",
            exception.Message);

        // La lesión no cambió ni se liberó.
        Assert.Equal(InjuryStatus.Active, injury.Status);
        Assert.Null(injury.ClearedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TransitionTo_WithoutClinicalNote_ThrowsDomainException(string? note)
    {
        var injury = CrearActiva();

        var exception = Assert.Throws<DomainException>(
            () => injury.TransitionTo(InjuryStatus.InRecovery, note, ChangedBy, ChangedAt));

        Assert.Equal("La nota clínica es obligatoria.", exception.Message);
        Assert.Equal(InjuryStatus.Active, injury.Status);
    }

    [Fact]
    public void TransitionTo_BackwardsFromInRecoveryToActive_ThrowsDomainException()
    {
        var injury = CrearEnRecuperacion();

        var exception = Assert.Throws<DomainException>(
            () => injury.TransitionTo(InjuryStatus.Active, "Vuelve a doler.", ChangedBy, ChangedAt));

        Assert.Equal("Transición de estado no permitida: de 'En recuperación' a 'Activa'.", exception.Message);
        Assert.Equal(InjuryStatus.InRecovery, injury.Status);
    }

    [Fact]
    public void TransitionTo_ToSameStatus_ThrowsDomainException()
    {
        var injury = CrearActiva();

        var exception = Assert.Throws<DomainException>(
            () => injury.TransitionTo(InjuryStatus.Active, "Sin cambios.", ChangedBy, ChangedAt));

        Assert.Equal("La lesión ya está en el estado 'Activa'.", exception.Message);
    }

    [Fact]
    public void TransitionTo_FromMedicallyCleared_ThrowsDomainExceptionBecauseItIsTerminal()
    {
        var injury = CrearEnRecuperacion();
        injury.TransitionTo(InjuryStatus.MedicallyCleared, "Alta confirmada.", ChangedBy, ChangedAt);

        var exception = Assert.Throws<DomainException>(
            () => injury.TransitionTo(InjuryStatus.InRecovery, "Recaída.", ChangedBy, ChangedAt));

        Assert.Equal("Transición de estado no permitida: de 'Alta médica' a 'En recuperación'.", exception.Message);
        Assert.Equal(InjuryStatus.MedicallyCleared, injury.Status);
    }

    [Fact]
    public void TransitionTo_WithEmptyChangedBy_ThrowsDomainExceptionWithoutChangingStatus()
    {
        var injury = CrearActiva();

        var exception = Assert.Throws<DomainException>(
            () => injury.TransitionTo(InjuryStatus.InRecovery, "Nota clínica.", Guid.Empty, ChangedAt));

        Assert.Equal("El usuario que realiza el cambio es inválido.", exception.Message);
        Assert.Equal(InjuryStatus.Active, injury.Status);
    }
}
