using Application.DTOs.Memberships;
using Application.DTOs.Routines;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Routines.RecordPerformedSets;
using Domain.Entities;
using Domain.Exceptions;
using Moq;
using Xunit;

namespace Tests;

public class RecordPerformedSetsCommandHandlerTests
{
    private readonly Mock<IStructureClient> _structureClientMock = new();
    private readonly Mock<ITrainingSessionRepository> _sessionRepoMock = new();
    private readonly Mock<IAthleteRecordRepository> _athleteRecordRepoMock = new();
    private readonly Mock<IPerformedSetRepository> _performedSetRepoMock = new();
    private readonly Mock<IRecordProposalRepository> _proposalRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();

    private readonly Guid _athleteId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();
    private readonly TrainingSession _session;

    public RecordPerformedSetsCommandHandlerTests()
    {
        var athleteDto = new AthleteMeDto
        {
            AthleteId = _athleteId,
            FirstName = "Lucas",
            LastName = "Gómez",
            FullName = "Lucas Gómez",
            Active = true,
            Categories = []
        };

        _structureClientMock
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(athleteDto);

        _session = TrainingSession.Create(Guid.NewGuid(), "Fuerza Día 1", 1, DayOfWeek.Monday);
        var prescribed = PrescribedExercise.Create(1, 4, 8, 80m, _session.Id, _exerciseId);
        _session.AddPrescribedExercise(prescribed);

        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(_athleteId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session);
    }

    private RecordPerformedSetsCommandHandler CreateHandler()
    {
        return new RecordPerformedSetsCommandHandler(
            _structureClientMock.Object,
            _sessionRepoMock.Object,
            _athleteRecordRepoMock.Object,
            _performedSetRepoMock.Object,
            _proposalRepoMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task CreatesProposal_WhenExceedsActiveRecord()
    {
        var currentRecord = AthleteRecord.Create(_athleteId, _exerciseId, 100m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentRecord);

        RecordProposal? capturedProposal = null;
        _proposalRepoMock
            .Setup(r => r.AddAsync(It.IsAny<RecordProposal>(), It.IsAny<CancellationToken>()))
            .Callback<RecordProposal, CancellationToken>((p, _) => capturedProposal = p);

        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = _exerciseId, Weight = 90m, Reps = 6 }
        ]);

        var result = await handler.Handle(command);

        Assert.Equal(1, result.RecordedSetsCount);
        Assert.Equal(1, result.ProposalsGeneratedCount);
        Assert.NotNull(capturedProposal);
        Assert.Equal(100m, capturedProposal.CurrentWeight);
        Assert.Equal(107.5m, capturedProposal.ProposedWeight);
        Assert.Equal(90m, capturedProposal.PerformedWeight);
        Assert.Equal(6, capturedProposal.PerformedReps);
        Assert.Equal(RecordProposalStatus.Pending, capturedProposal.Status);

        Assert.Equal(100m, currentRecord.Weight);
        Assert.True(currentRecord.IsActive);
        _athleteRecordRepoMock.Verify(r => r.UpdateAsync(It.IsAny<AthleteRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _performedSetRepoMock.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<PerformedSet>>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoProposal_WhenRepsExceedTen()
    {
        var currentRecord = AthleteRecord.Create(_athleteId, _exerciseId, 100m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentRecord);

        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = _exerciseId, Weight = 90m, Reps = 12 }
        ]);

        var result = await handler.Handle(command);

        Assert.Equal(1, result.RecordedSetsCount);
        Assert.Equal(0, result.ProposalsGeneratedCount);
        _proposalRepoMock.Verify(r => r.AddAsync(It.IsAny<RecordProposal>(), It.IsAny<CancellationToken>()), Times.Never);
        _performedSetRepoMock.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<PerformedSet>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoProposal_WhenNotExceedingRecord()
    {
        var currentRecord = AthleteRecord.Create(_athleteId, _exerciseId, 110m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentRecord);

        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = _exerciseId, Weight = 90m, Reps = 6 }
        ]);

        var result = await handler.Handle(command);

        Assert.Equal(1, result.RecordedSetsCount);
        Assert.Equal(0, result.ProposalsGeneratedCount);
        _proposalRepoMock.Verify(r => r.AddAsync(It.IsAny<RecordProposal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoProposal_WhenProposedEqualsActiveRecord()
    {
        var currentRecord = AthleteRecord.Create(_athleteId, _exerciseId, 107.5m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentRecord);

        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = _exerciseId, Weight = 90m, Reps = 6 }
        ]);

        var result = await handler.Handle(command);

        Assert.Equal(1, result.RecordedSetsCount);
        Assert.Equal(0, result.ProposalsGeneratedCount);
        _proposalRepoMock.Verify(r => r.AddAsync(It.IsAny<RecordProposal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoProposal_WhenNoActiveRecord()
    {
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteRecord?)null);

        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = _exerciseId, Weight = 90m, Reps = 6 }
        ]);

        var result = await handler.Handle(command);

        Assert.Equal(1, result.RecordedSetsCount);
        Assert.Equal(0, result.ProposalsGeneratedCount);
        _proposalRepoMock.Verify(r => r.AddAsync(It.IsAny<RecordProposal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdatesPendingProposal_WhenHigher()
    {
        var currentRecord = AthleteRecord.Create(_athleteId, _exerciseId, 100m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentRecord);

        var existingPending = RecordProposal.Create(_athleteId, _exerciseId, 100m, 105m, 90m, 5);
        _proposalRepoMock
            .Setup(r => r.GetPendingAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPending);

        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = _exerciseId, Weight = 90m, Reps = 6 }
        ]);

        var result = await handler.Handle(command);

        Assert.Equal(1, result.ProposalsGeneratedCount);
        Assert.Equal(107.5m, existingPending.ProposedWeight);
        Assert.Equal(90m, existingPending.PerformedWeight);
        Assert.Equal(6, existingPending.PerformedReps);
        _proposalRepoMock.Verify(r => r.UpdateAsync(existingPending, It.IsAny<CancellationToken>()), Times.Once);
        _proposalRepoMock.Verify(r => r.AddAsync(It.IsAny<RecordProposal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task KeepsPendingProposal_WhenLower()
    {
        var currentRecord = AthleteRecord.Create(_athleteId, _exerciseId, 100m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentRecord);

        var existingPending = RecordProposal.Create(_athleteId, _exerciseId, 100m, 110m, 95m, 5);
        _proposalRepoMock
            .Setup(r => r.GetPendingAsync(_athleteId, _exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPending);

        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = _exerciseId, Weight = 90m, Reps = 6 }
        ]);

        var result = await handler.Handle(command);

        Assert.Equal(0, result.ProposalsGeneratedCount);
        Assert.Equal(110m, existingPending.ProposedWeight);
        _proposalRepoMock.Verify(r => r.UpdateAsync(It.IsAny<RecordProposal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Throws_WhenExerciseNotInSession()
    {
        var unknownExerciseId = Guid.NewGuid();
        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = unknownExerciseId, Weight = 90m, Reps = 6 }
        ]);

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(command));
        Assert.Equal("Uno o más ejercicios no pertenecen a la sesión de hoy.", ex.Message);
    }

    [Fact]
    public async Task Throws_WhenNoSessionToday()
    {
        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(_athleteId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingSession?)null);

        var handler = CreateHandler();
        var command = new RecordPerformedSetsCommand([
            new PerformedSetInputDto { ExerciseId = _exerciseId, Weight = 90m, Reps = 6 }
        ]);

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(command));
        Assert.Equal("El atleta no tiene una sesión programada para el día de hoy.", ex.Message);
    }
}

