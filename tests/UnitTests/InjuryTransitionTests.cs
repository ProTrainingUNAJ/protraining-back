using ProTraining.Common;
using ProTraining.Domain.Entities;
using Xunit;

namespace ProTraining.UnitTests;

public class InjuryTransitionTests
{
    [Fact]
    public void NewInjury_ShouldHaveActiveStatus()
    {
        var injury = new Injury
        {
            BodyZone = "Rodilla derecha",
            Notes = "Molestia al flexionar"
        };

        Assert.Equal(InjuryStatus.Active, injury.Status);
    }

    [Fact]
    public void ActiveInjury_TransitionToInRecovery_ShouldSucceed()
    {
        var injury = new Injury
        {
            BodyZone = "Hombro izquierdo",
            Notes = "Sobrecarga en press militar"
        };

        injury.TransitionTo(InjuryStatus.InRecovery, "Inicia kinesiología");

        Assert.Equal(InjuryStatus.InRecovery, injury.Status);
        Assert.Contains("Inicia kinesiología", injury.Notes);
    }

    [Fact]
    public void ActiveInjury_TransitionDirectlyToCleared_ShouldThrowDomainException()
    {
        // Regla CU-08 de la propuesta ProTraining:
        // "Si intenta pasar de Activa a Alta directamente, el sistema lo rechaza;
        // debe pasar por En recuperación con una nota."
        var injury = new Injury
        {
            BodyZone = "Tobillo derecho",
            Notes = "Esguince grado 1"
        };

        var exception = Assert.Throws<DomainException>(() =>
            injury.TransitionTo(InjuryStatus.Cleared, "Dado de alta directo"));

        Assert.Contains("no puede pasar directamente a Alta", exception.Message);
        Assert.Equal(InjuryStatus.Active, injury.Status);
    }

    [Fact]
    public void InRecoveryInjury_TransitionToCleared_ShouldSucceed()
    {
        var injury = new Injury
        {
            BodyZone = "Tobillo derecho",
            Notes = "Esguince grado 1"
        };

        injury.TransitionTo(InjuryStatus.InRecovery, "Ejercicios de propiocepción");
        injury.TransitionTo(InjuryStatus.Cleared, "Alta médica otorgada");

        Assert.Equal(InjuryStatus.Cleared, injury.Status);
    }
}
