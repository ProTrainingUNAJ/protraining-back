using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.TrainingPlans.AssignPlan;
using Domain.Entities;
using Moq;

namespace Entrenamiento.Tests;

// KAN-431: asignar una planificación a una categoría o un atleta de otro PF (o inexistentes)
// responde 404 y no guarda nada, igual que un recurso propio que no existe.
public class AssignPlanCommandHandlerTests
{
    private static readonly Guid PlanId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 11, 2);

    private readonly Mock<ITrainingPlanRepository> _plans = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IStructureClient> _structure = new();

    public AssignPlanCommandHandlerTests()
    {
        _plans.Setup(repo => repo.ExistsAsync(PlanId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private AssignPlanCommandHandler CreateHandler() => new(_plans.Object, _unitOfWork.Object, _structure.Object);

    [Fact]
    public async Task Given_OwnCategory_When_Assigning_Then_SavesAssignment()
    {
        var categoryId = Guid.NewGuid();
        _structure.Setup(client => client.IsOwnCategoryAsync(categoryId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await CreateHandler().Handle(new AssignPlanCommand(PlanId, StartDate, categoryId, null));

        _plans.Verify(repo => repo.AddAssignmentAsync(
            It.Is<PlanAssignment>(assignment => assignment.CategoryId == categoryId), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_CategoryOfAnotherPf_When_Assigning_Then_ThrowsNotFoundAndSavesNothing()
    {
        var categoryId = Guid.NewGuid();
        _structure.Setup(client => client.IsOwnCategoryAsync(categoryId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateHandler().Handle(new AssignPlanCommand(PlanId, StartDate, categoryId, null)));

        AssertNothingSaved();
    }

    [Fact]
    public async Task Given_OwnAthlete_When_Assigning_Then_SavesAssignment()
    {
        var athleteId = Guid.NewGuid();
        _structure.Setup(client => client.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athleteId, "Lucas Gómez"));

        await CreateHandler().Handle(new AssignPlanCommand(PlanId, StartDate, null, athleteId));

        _plans.Verify(repo => repo.AddAssignmentAsync(
            It.Is<PlanAssignment>(assignment => assignment.AthleteId == athleteId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_AthleteOfAnotherPf_When_Assigning_Then_ThrowsNotFoundAndSavesNothing()
    {
        var athleteId = Guid.NewGuid();
        _structure.Setup(client => client.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StructureAthleteDto?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateHandler().Handle(new AssignPlanCommand(PlanId, StartDate, null, athleteId)));

        AssertNothingSaved();
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_PlanOfAnotherPf_When_Assigning_Then_ThrowsNotFoundWithoutAskingStructure()
    {
        var foreignPlanId = Guid.NewGuid();
        _plans.Setup(repo => repo.ExistsAsync(foreignPlanId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateHandler().Handle(new AssignPlanCommand(foreignPlanId, StartDate, Guid.NewGuid(), null)));

        _structure.VerifyNoOtherCalls();
        AssertNothingSaved();
    }

    private void AssertNothingSaved()
    {
        _plans.Verify(repo => repo.AddAssignmentAsync(It.IsAny<PlanAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
