using Domain.Entities;
using Domain.Exceptions;

namespace Seguimiento.Tests;

// KAN-568: máquina de estados estricta y secuencial Planned → InProgress → Closed,
// encapsulada en FieldTestSession.TransitionTo. Sin saltos ni reaperturas; toda
// violación de regla sale del dominio como DomainException con mensaje en español.
public class FieldTestSessionTransitionTests
{
    private static FieldTestSession CrearPlanificada() =>
        new()
        {
            CategoryId = Guid.NewGuid(),
            Date = new DateTime(2026, 11, 4),
            Name = "Evaluación de pretemporada",
            Status = FieldTestSessionStatus.Planned
        };

    private static FieldTestSession CrearCerrada()
    {
        var session = CrearPlanificada();
        session.TransitionTo(FieldTestSessionStatus.InProgress);
        session.TransitionTo(FieldTestSessionStatus.Closed);
        return session;
    }

    // ---------- Transiciones permitidas ----------

    [Fact]
    public void TransitionTo_FromPlannedToInProgress_ChangesStatus()
    {
        var session = CrearPlanificada();

        session.TransitionTo(FieldTestSessionStatus.InProgress);

        Assert.Equal(FieldTestSessionStatus.InProgress, session.Status);
    }

    [Fact]
    public void TransitionTo_FromInProgressToClosed_ChangesStatus()
    {
        var session = CrearPlanificada();
        session.TransitionTo(FieldTestSessionStatus.InProgress);

        session.TransitionTo(FieldTestSessionStatus.Closed);

        Assert.Equal(FieldTestSessionStatus.Closed, session.Status);
    }

    // El flujo completo del CA: Planificada → En curso → Cerrada.
    [Fact]
    public void TransitionTo_FullAllowedFlow_MovesPlannedToInProgressToClosed()
    {
        var session = CrearPlanificada();

        session.TransitionTo(FieldTestSessionStatus.InProgress);
        session.TransitionTo(FieldTestSessionStatus.Closed);

        Assert.Equal(FieldTestSessionStatus.Closed, session.Status);
    }

    // ---------- Violaciones de regla (DomainException → 400) ----------

    // Sin salto directo de Planned a Closed.
    [Fact]
    public void TransitionTo_FromPlannedToClosed_ThrowsFriendlySpanishMessage()
    {
        var session = CrearPlanificada();

        var error = Assert.Throws<DomainException>(() =>
            session.TransitionTo(FieldTestSessionStatus.Closed));

        Assert.Equal(
            "Transición de estado no permitida: de 'Planificada' a 'Cerrada'. " +
            "Orden válido: 'Planificada' → 'En curso' → 'Cerrada'.",
            error.Message);
        Assert.Equal(FieldTestSessionStatus.Planned, session.Status);
    }

    // Sin reaperturas desde Closed.
    [Fact]
    public void TransitionTo_FromClosedToInProgress_ThrowsClosedMessage()
    {
        var session = CrearCerrada();

        var error = Assert.Throws<DomainException>(() =>
            session.TransitionTo(FieldTestSessionStatus.InProgress));

        Assert.Equal("La sesión está cerrada: no se puede cambiar su estado.", error.Message);
        Assert.Equal(FieldTestSessionStatus.Closed, session.Status);
    }

    [Fact]
    public void TransitionTo_FromClosedToPlanned_ThrowsClosedMessage()
    {
        var session = CrearCerrada();

        var error = Assert.Throws<DomainException>(() =>
            session.TransitionTo(FieldTestSessionStatus.Planned));

        Assert.Equal("La sesión está cerrada: no se puede cambiar su estado.", error.Message);
        Assert.Equal(FieldTestSessionStatus.Closed, session.Status);
    }

    // Repetir el estado actual también es una transición inválida.
    [Fact]
    public void TransitionTo_ToSameStatus_ThrowsAlreadyInStatusMessage()
    {
        var session = CrearPlanificada();
        session.TransitionTo(FieldTestSessionStatus.InProgress);

        var error = Assert.Throws<DomainException>(() =>
            session.TransitionTo(FieldTestSessionStatus.InProgress));

        Assert.Equal("La sesión ya está en el estado 'En curso'.", error.Message);
        Assert.Equal(FieldTestSessionStatus.InProgress, session.Status);
    }
}
