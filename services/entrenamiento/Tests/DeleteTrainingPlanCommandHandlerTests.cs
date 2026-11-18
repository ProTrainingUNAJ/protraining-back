using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.TrainingPlans.DeletePlan;
using Domain.Entities;
using Moq;

namespace Entrenamiento.Tests;

public class DeleteTrainingPlanCommandHandlerTests
{
    private readonly Mock<ITrainingPlanRepository> _plans = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private DeleteTrainingPlanCommandHandler CreateHandler() => new(_plans.Object, _unitOfWork.Object);

    [Fact]
    public async Task Given_ExistingPlan_When_Deleting_Then_RemovesPlanAndSavesChanges()
    {
        var planId = Guid.NewGuid();
        var plan = TrainingPlan.Create("Plan Fuerza Base", "Objetivo fuerza");
        _plans.Setup(repo => repo.GetByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        await CreateHandler().Handle(new DeleteTrainingPlanCommand(planId));

        _plans.Verify(repo => repo.Remove(plan), Times.Once);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_NonExistingPlan_When_Deleting_Then_ThrowsNotFound()
    {
        var planId = Guid.NewGuid();
        _plans.Setup(repo => repo.GetByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingPlan?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => CreateHandler().Handle(new DeleteTrainingPlanCommand(planId)));

        Assert.Contains(planId.ToString(), ex.Message);
        _plans.Verify(repo => repo.Remove(It.IsAny<TrainingPlan>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
