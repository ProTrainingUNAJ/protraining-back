using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.RecordProposals.ApproveRecordProposal;
using Application.UseCases.RecordProposals.DiscardRecordProposal;
using Application.UseCases.RecordProposals.ListRecordProposals;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Tests;

public class RecordProposalUseCasesTests
{
    private sealed class TestCurrentUser(Guid? pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Mock<IStructureClient> _structureClient = new();

    public RecordProposalUseCasesTests()
    {
        _structureClient
            .Setup(c => c.GetAthleteByIdAsync(_athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(_athleteId, "Lucas"));
    }

    private AppDbContext CreateContext(Guid? pfId = null) => CreateContextFor(pfId ?? _pfId);

    private AppDbContext CreateContextFor(Guid? pfId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new AppDbContext(options, new TestCurrentUser(pfId));
    }

    private async Task<Exercise> SeedExerciseAsync(string name)
    {
        var exercise = Exercise.Create(name, "Piernas");
        await using var context = CreateContextFor(null);
        await context.Exercises.AddAsync(exercise);
        await context.SaveChangesAsync();
        return exercise;
    }

    private ApproveRecordProposalCommandHandler CreateApproveHandler(AppDbContext context) =>
        new(new RecordProposalRepository(context), new AthleteRecordRepository(context), new UnitOfWork(context));

    private DiscardRecordProposalCommandHandler CreateDiscardHandler(AppDbContext context) =>
        new(new RecordProposalRepository(context), new UnitOfWork(context));

    private ListRecordProposalsQueryHandler CreateListHandler(AppDbContext context) =>
        new(new RecordProposalRepository(context), _structureClient.Object);

    [Fact]
    public async Task Approve_PendingProposal_CreatesEpleyRecordAndDeactivatesPrevious()
    {
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var initialRecord = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5);
            await context.AthleteRecords.AddAsync(initialRecord);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            var result = await CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId));
            Assert.Equal(107.5m, result.WeightOneRepMaxKg);
            Assert.Equal("Epley", result.Origin);
            Assert.True(result.IsCurrent);
        }

        await using (var verify = CreateContext())
        {
            var records = await verify.AthleteRecords.ToListAsync();
            Assert.Equal(2, records.Count);

            var active = Assert.Single(records, r => r.IsActive);
            Assert.Equal(107.5m, active.Weight);
            Assert.Equal(RecordSource.Epley, active.Source);

            var inactive = Assert.Single(records, r => !r.IsActive);
            Assert.Equal(100m, inactive.Weight);

            var updatedProposal = await verify.RecordProposals.SingleAsync(p => p.Id == proposalId);
            Assert.Equal(RecordProposalStatus.Approved, updatedProposal.Status);
        }
    }

    [Fact]
    public async Task Approve_WhenCurrentRecordChanged_ThrowsConflict409()
    {
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            // El récord vigente cambió manualmente a 105 kg, pero la propuesta se creó con 100 kg
            var changedRecord = AthleteRecord.Create(_athleteId, exercise.Id, 105m, RecordSource.Tested, DateTime.UtcNow);
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5);
            await context.AthleteRecords.AddAsync(changedRecord);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId)));

            Assert.Equal("El récord cambió; revisá la propuesta", ex.Message);
        }

        await using (var verify = CreateContext())
        {
            var active = await verify.AthleteRecords.SingleAsync(r => r.IsActive);
            Assert.Equal(105m, active.Weight);

            var proposal = await verify.RecordProposals.SingleAsync(p => p.Id == proposalId);
            Assert.Equal(RecordProposalStatus.Pending, proposal.Status);
        }
    }

    [Fact]
    public async Task Approve_WhenCurrentRecordIdMatchesActiveRecord_UpdatesRecordAndMarksProposalApproved()
    {
        // KAN-566: Verificación por ID de récord activo
        var exercise = await SeedExerciseAsync("Press de Banca");

        Guid proposalId;
        Guid recordId;
        await using (var context = CreateContext())
        {
            var initialRecord = AthleteRecord.Create(_athleteId, exercise.Id, 90m, RecordSource.Tested, DateTime.UtcNow);
            await context.AthleteRecords.AddAsync(initialRecord);
            await context.SaveChangesAsync();
            recordId = initialRecord.Id;

            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 90m, 97.5m, 85m, 6, recordId);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            var result = await CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId));
            Assert.Equal(97.5m, result.WeightOneRepMaxKg);
            Assert.Equal("Epley", result.Origin);
            Assert.True(result.IsCurrent);
        }

        await using (var verify = CreateContext())
        {
            var active = await verify.AthleteRecords.SingleAsync(r => r.IsActive);
            Assert.Equal(97.5m, active.Weight);
            Assert.Equal(RecordSource.Epley, active.Source);

            var oldRecord = await verify.AthleteRecords.SingleAsync(r => r.Id == recordId);
            Assert.False(oldRecord.IsActive);

            var updatedProposal = await verify.RecordProposals.SingleAsync(p => p.Id == proposalId);
            Assert.Equal(RecordProposalStatus.Approved, updatedProposal.Status);
        }
    }

    [Fact]
    public async Task Approve_WhenPfRecordedManualRecordWithSameWeight_ThrowsConflict409BecauseIdChanged()
    {
        // KAN-566: Caso crítico de negocio: el PF cargó a mano otro récord en el medio con el MISMO peso (100 kg)
        // La propuesta tenía el ID del récord anterior, por lo que activeRecord.Id != proposal.CurrentRecordId
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var originalRecord = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow.AddDays(-2));
            await context.AthleteRecords.AddAsync(originalRecord);
            await context.SaveChangesAsync();

            // La propuesta se calculó teniendo originalRecord activo
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 110m, 95m, 5, originalRecord.Id);
            await context.RecordProposals.AddAsync(proposal);

            // En el medio, el PF carga manualmente otro récord con el MISMO peso (100m)
            originalRecord.Deactivate();
            var manualRecord = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow.AddDays(-1));
            await context.AthleteRecords.AddAsync(manualRecord);

            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            // Debe fallar con 409 Conflict aunque el peso sea idéntico (100 kg)
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId)));

            Assert.Equal("El récord cambió; revisá la propuesta", ex.Message);
        }

        await using (var verify = CreateContext())
        {
            var proposal = await verify.RecordProposals.SingleAsync(p => p.Id == proposalId);
            Assert.Equal(RecordProposalStatus.Pending, proposal.Status);
        }
    }

    [Fact]
    public async Task Approve_WhenActiveRecordIsMissing_ThrowsConflict409()
    {
        // KAN-566: Si el récord activo ya no existe en el sistema
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var originalRecord = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
            originalRecord.Deactivate(); // Ya no hay ningún récord activo
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 110m, 95m, 5, originalRecord.Id);
            await context.AthleteRecords.AddAsync(originalRecord);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId)));

            Assert.Equal("El récord cambió; revisá la propuesta", ex.Message);
        }
    }

    [Fact]
    public async Task Approve_WhenLegacyProposalWithNullCurrentRecordId_DoesNotThrowNullReferenceAndApprovesIfWeightMatches()
    {
        // KAN-566 (Anti-rebote PM): Propuesta previa con CurrentRecordId nulo no lanza NullReferenceException
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var initialRecord = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
            // CurrentRecordId es nulo explícitamente (emulando datos previos a la migración)
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5, currentRecordId: null);
            await context.AthleteRecords.AddAsync(initialRecord);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            var result = await CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId));
            Assert.Equal(107.5m, result.WeightOneRepMaxKg);
            Assert.Equal("Epley", result.Origin);
        }

        await using (var verify = CreateContext())
        {
            var updatedProposal = await verify.RecordProposals.SingleAsync(p => p.Id == proposalId);
            Assert.Equal(RecordProposalStatus.Approved, updatedProposal.Status);
        }
    }

    [Fact]
    public async Task Approve_WhenLegacyProposalWithNullCurrentRecordIdAndWeightChanged_ThrowsConflict409()
    {
        // KAN-566 (Anti-rebote PM): Propuesta previa con CurrentRecordId nulo pero cuyo peso cambió
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var initialRecord = AthleteRecord.Create(_athleteId, exercise.Id, 105m, RecordSource.Tested, DateTime.UtcNow);
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5, currentRecordId: null);
            await context.AthleteRecords.AddAsync(initialRecord);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId)));

            Assert.Equal("El récord cambió; revisá la propuesta", ex.Message);
        }
    }

    [Fact]
    public async Task Approve_WhenProposalNotPending_ThrowsConflict409()
    {
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var record = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5);
            proposal.Discard(); // ya no está pendiente
            await context.AthleteRecords.AddAsync(record);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId)));

            Assert.Equal("La propuesta no está pendiente.", ex.Message);
        }
    }

    [Fact]
    public async Task Approve_FromAnotherPf_ThrowsNotFound404()
    {
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var record = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5);
            await context.AthleteRecords.AddAsync(record);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        // Otro PF con distinto PfId
        var otherPfId = Guid.NewGuid();
        await using (var context = CreateContextFor(otherPfId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                CreateApproveHandler(context).Handle(new ApproveRecordProposalCommand(proposalId)));
        }
    }

    [Fact]
    public async Task Discard_PendingProposal_MarksAsDiscarded()
    {
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var record = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5);
            await context.AthleteRecords.AddAsync(record);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            await CreateDiscardHandler(context).Handle(new DiscardRecordProposalCommand(proposalId));
        }

        await using (var verify = CreateContext())
        {
            var proposal = await verify.RecordProposals.SingleAsync(p => p.Id == proposalId);
            Assert.Equal(RecordProposalStatus.Discarded, proposal.Status);

            var active = await verify.AthleteRecords.SingleAsync(r => r.IsActive);
            Assert.Equal(100m, active.Weight);
        }
    }

    [Fact]
    public async Task Discard_WhenProposalNotPending_ThrowsConflict409()
    {
        var exercise = await SeedExerciseAsync("Sentadilla");

        Guid proposalId;
        await using (var context = CreateContext())
        {
            var record = AthleteRecord.Create(_athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
            var proposal = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5);
            proposal.Approve();
            await context.AthleteRecords.AddAsync(record);
            await context.RecordProposals.AddAsync(proposal);
            await context.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        await using (var context = CreateContext())
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                CreateDiscardHandler(context).Handle(new DiscardRecordProposalCommand(proposalId)));

            Assert.Equal("La propuesta no está pendiente.", ex.Message);
        }
    }

    [Fact]
    public async Task List_WithPendingStatus_ReturnsProposalsWithDetails()
    {
        var exercise = await SeedExerciseAsync("Sentadilla");

        await using (var context = CreateContext())
        {
            var pending = RecordProposal.Create(_athleteId, exercise.Id, 100m, 107.5m, 90m, 5);
            var discarded = RecordProposal.Create(_athleteId, exercise.Id, 80m, 90m, 80m, 4);
            discarded.Discard();

            await context.RecordProposals.AddRangeAsync(pending, discarded);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var result = await CreateListHandler(context).Handle(new ListRecordProposalsQuery(RecordProposalStatus.Pending));

            var item = Assert.Single(result);
            Assert.Equal(_athleteId, item.AthleteId);
            Assert.Equal("Lucas", item.AthleteName);
            Assert.Equal(exercise.Id, item.ExerciseId);
            Assert.Equal("Sentadilla", item.ExerciseName);
            Assert.Equal(100m, item.CurrentWeight);
            Assert.Equal(107.5m, item.ProposedWeight);
            Assert.Equal(90m, item.PerformedWeight);
            Assert.Equal(5, item.PerformedReps);
            Assert.Equal("Pending", item.Status);
        }
    }
}
