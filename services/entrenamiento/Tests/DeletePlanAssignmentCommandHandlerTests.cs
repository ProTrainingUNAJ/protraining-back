using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.TrainingPlans.DeleteAssignment;
using Domain.Entities;
using Moq;

namespace Entrenamiento.Tests;

public class DeletePlanAssignmentCommandHandlerTests
{
    private readonly Mock<ITrainingPlanRepository> _plans = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private DeletePlanAssignmentCommandHandler CreateHandler() => new(_plans.Object, _unitOfWork.Object);

    [Fact]
    public async Task Given_ExistingAssignment_When_Deleting_Then_RemovesAndSavesChanges()
    {
        var assignmentId = Guid.NewGuid();
        var assignment = PlanAssignment.Create(Guid.NewGuid(), new DateOnly(2026, 11, 2), athleteId: Guid.NewGuid());
        _plans.Setup(repo => repo.GetAssignmentByIdAsync(assignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignment);

        await CreateHandler().Handle(new DeletePlanAssignmentCommand(assignmentId));

        _plans.Verify(repo => repo.RemoveAssignment(assignment), Times.Once);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_NonExistingAssignment_When_Deleting_Then_ThrowsNotFound()
    {
        var assignmentId = Guid.NewGuid();
        _plans.Setup(repo => repo.GetAssignmentByIdAsync(assignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanAssignment?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateHandler().Handle(new DeletePlanAssignmentCommand(assignmentId)));

        _plans.Verify(repo => repo.RemoveAssignment(It.IsAny<PlanAssignment>()), Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
