using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.UseCases.Athletes.UpdateAthletePhysicalProfile;
using Application.UseCases.Athletes.UpdateMyPhysicalProfile;
using Domain.Entities;
using Domain.Exceptions;
using Moq;

namespace Estructura.Tests;

/// <summary>
/// KAN-494: el perfil físico de Estructura es la fuente canónica de peso y objetivo.
/// Cubre validación de dominio, etiquetas cerradas, mapeo al DTO y los dos comandos de escritura.
/// </summary>
public sealed class AthletePhysicalProfileTests
{
    private static Athlete BuildAthlete() => Athlete.CreateAdult(
        "Lucas",
        "Fernandez",
        "lucas@club.com",
        "+5491155551234");

    [Fact]
    public void UpdatePhysicalProfile_SetsWeightGoalAndTimestamp()
    {
        var athlete = BuildAthlete();

        athlete.UpdatePhysicalProfile(70.456m, AthleticGoalLabels.GainMuscle);

        Assert.Equal(70.46m, athlete.WeightKg);
        Assert.Equal(AthleticGoal.GainMuscle, athlete.AthleticGoal);
        Assert.NotNull(athlete.PhysicalProfileUpdatedAt);
    }

    [Fact]
    public void UpdatePhysicalProfile_NullWeightAndGoal_AreStoredAsMissing()
    {
        var athlete = BuildAthlete();
        athlete.UpdatePhysicalProfile(70m, AthleticGoalLabels.GainMuscle);

        athlete.UpdatePhysicalProfile(null, null);

        Assert.Null(athlete.WeightKg);
        Assert.Equal(AthleticGoal.NotReported, athlete.AthleticGoal);
        Assert.Equal(AthleticGoalLabels.NotReported, AthleticGoalLabels.ToLabel(athlete.AthleticGoal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(401)]
    public void UpdatePhysicalProfile_InvalidWeight_Throws(double weight)
    {
        var athlete = BuildAthlete();

        Assert.Throws<DomainException>(() =>
            athlete.UpdatePhysicalProfile((decimal)weight, AthleticGoalLabels.GainMuscle));
    }

    [Fact]
    public void UpdatePhysicalProfile_UnknownGoal_ThrowsWithoutChangingState()
    {
        var athlete = BuildAthlete();
        athlete.UpdatePhysicalProfile(70m, AthleticGoalLabels.Maintain);

        var exception = Assert.Throws<DomainException>(() =>
            athlete.UpdatePhysicalProfile(80m, "Perder peso"));

        Assert.Contains("objetivo deportivo", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(70m, athlete.WeightKg);
        Assert.Equal(AthleticGoal.Maintain, athlete.AthleticGoal);
    }

    [Fact]
    public void AthleticGoalLabels_ParseClosedVocabulary()
    {
        Assert.True(AthleticGoalLabels.TryParse("ganar masa muscular", out var gain));
        Assert.Equal(AthleticGoal.GainMuscle, gain);
        Assert.True(AthleticGoalLabels.TryParse(null, out var none));
        Assert.Equal(AthleticGoal.NotReported, none);
        Assert.False(AthleticGoalLabels.TryParse("Perder peso", out _));
        Assert.Equal(AthleticGoalLabels.NotReported, AthleticGoalLabels.ToLabel(null));
    }

    [Fact]
    public void ToDto_ExposesWeightAndSpanishLabel()
    {
        var athlete = BuildAthlete();
        athlete.UpdatePhysicalProfile(70m, AthleticGoalLabels.Performance);

        var dto = AthleteMappings.ToDto(athlete);

        Assert.Equal(70m, dto.WeightKg);
        Assert.Equal(AthleticGoalLabels.Performance, dto.AthleticGoal);
        Assert.Equal(athlete.PhysicalProfileUpdatedAt, dto.PhysicalProfileUpdatedAt);
    }

    [Fact]
    public async Task PfCommand_PersistsAndReturnsDto()
    {
        var athlete = BuildAthlete();
        var repository = new Mock<IAthleteRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        repository.Setup(r => r.GetByIdAsync(athlete.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);

        var handler = new UpdateAthletePhysicalProfileCommandHandler(repository.Object, unitOfWork.Object);

        var result = await handler.HandleAsync(new UpdateAthletePhysicalProfileCommand(
            athlete.Id, 70m, AthleticGoalLabels.GainMuscle));

        Assert.Equal(70m, result.WeightKg);
        Assert.Equal(AthleticGoalLabels.GainMuscle, result.AthleticGoal);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PfCommand_UnknownAthlete_ThrowsAndDoesNotSave()
    {
        var repository = new Mock<IAthleteRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new UpdateAthletePhysicalProfileCommandHandler(repository.Object, unitOfWork.Object);

        await Assert.ThrowsAsync<AthleteNotFoundException>(() => handler.HandleAsync(
            new UpdateAthletePhysicalProfileCommand(Guid.NewGuid(), 70m, AthleticGoalLabels.GainMuscle)));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MyCommand_UsesTokenAccountAndReturnsLabel()
    {
        var accountId = Guid.NewGuid();
        var athlete = BuildAthlete();
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IAthleteRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        currentUser.Setup(u => u.UserId).Returns(accountId);
        repository.Setup(r => r.GetByAccountIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);

        var handler = new UpdateMyPhysicalProfileCommandHandler(
            currentUser.Object, repository.Object, unitOfWork.Object);

        var result = await handler.HandleAsync(
            new UpdateMyPhysicalProfileCommand(70m, AthleticGoalLabels.GainMuscle));

        Assert.Equal(70m, result.WeightKg);
        Assert.Equal(AthleticGoalLabels.GainMuscle, result.AthleticGoal);
        Assert.NotNull(result.PhysicalProfileUpdatedAt);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MyCommand_WithoutToken_ThrowsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        var repository = new Mock<IAthleteRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new UpdateMyPhysicalProfileCommandHandler(
            currentUser.Object, repository.Object, unitOfWork.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.HandleAsync(
            new UpdateMyPhysicalProfileCommand(70m, AthleticGoalLabels.GainMuscle)));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
