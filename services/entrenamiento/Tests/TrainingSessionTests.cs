using Domain.Entities;
using Domain.Exceptions;
using Xunit;

namespace Tests;

public class TrainingSessionTests
{
    [Fact]
    public void UpdateCoachNotes_DeberiaActualizarLaNotaDelPf()
    {
        var session = TrainingSession.Create(
            Guid.NewGuid(),
            "Sesión Miércoles",
            1,
            DayOfWeek.Wednesday
        );

        session.UpdateCoachNotes("Controlar técnica en descenso");

        Assert.Equal("Controlar técnica en descenso", session.CoachNotes);
    }

    [Fact]
    public void PlanAssignment_NoDeberiaPermitirCategoriaYAtletaSimultaneos()
    {
        Assert.Throws<DomainException>(() =>
            PlanAssignment.Create(
                Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.UtcNow),
                categoryId: Guid.NewGuid(),
                athleteId: Guid.NewGuid()
            )
        );
    }
}
