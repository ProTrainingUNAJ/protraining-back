using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

namespace Asistente.Tests.Security;

// KAN-430: un PF no ve ni toca los borradores del Asistente de otro PF. Se prueba contra Postgres real
// con el filtro global por PF. El 404 por HTTP se suma cuando exista GET /plan-drafts/{id} (KAN-383).
[Trait("Category", "Isolation")]
[Trait("Category", "RequiresDocker")]
public sealed class RoutineDraftIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();
    private readonly Guid _athleteOfPfB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext(_pfBId);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Given_DraftOfPfB_When_PfAQueriesByIdOrByAthlete_Then_FindsNothing()
    {
        // arrange: PF-B guarda un borrador para su atleta
        var draftId = await SaveDraftAsPfBAsync();

        // act: PF-A lo busca por id y por atleta
        await using var contextPfA = CreateContext(_pfAId);
        var repositoryPfA = new RoutineDraftRepository(contextPfA);
        var byId = await repositoryPfA.GetByIdAsync(draftId);
        var byAthlete = await repositoryPfA.GetByAthleteIdAsync(_athleteOfPfB);

        // assert: para PF-A el borrador no existe (el endpoint responderá 404, nunca 403)
        Assert.Null(byId);
        Assert.Empty(byAthlete);

        // y PF-B lo sigue viendo
        await using var contextPfB = CreateContext(_pfBId);
        Assert.NotNull(await new RoutineDraftRepository(contextPfB).GetByIdAsync(draftId));
    }

    [Fact]
    public async Task Given_DraftOfPfB_When_PfACreatesItsOwnDraftWithTheSameAthleteId_Then_EachPfOnlySeesItsOwn()
    {
        // arrange: PF-B tiene un borrador; PF-A guarda otro para el mismo id de atleta (un id adivinado)
        var draftOfPfB = await SaveDraftAsPfBAsync();

        await using (var contextPfA = CreateContext(_pfAId))
        {
            await new RoutineDraftRepository(contextPfA).AddAsync(NewDraft("Objetivo de PF-A"));
            await contextPfA.SaveChangesAsync();
        }

        // act
        await using var readPfA = CreateContext(_pfAId);
        var draftsSeenByPfA = await new RoutineDraftRepository(readPfA).GetByAthleteIdAsync(_athleteOfPfB);

        // assert: PF-A ve solo el suyo, guardado con su pf_id; el de PF-B no aparece
        var onlyDraft = Assert.Single(draftsSeenByPfA);
        Assert.Equal("Objetivo de PF-A", onlyDraft.Goal);
        Assert.Equal(_pfAId, onlyDraft.PfId);
        Assert.DoesNotContain(draftsSeenByPfA, draft => draft.Id == draftOfPfB);
    }

    private async Task<Guid> SaveDraftAsPfBAsync()
    {
        await using var contextPfB = CreateContext(_pfBId);
        var draft = NewDraft("Objetivo de PF-B");
        await new RoutineDraftRepository(contextPfB).AddAsync(draft);
        await contextPfB.SaveChangesAsync();
        return draft.Id;
    }

    private RoutineDraft NewDraft(string goal) => RoutineDraft.Create(
        athleteId: _athleteOfPfB,
        goal: goal,
        proposedTitle: "Fuerza base",
        justificationSummary: "Progresión lineal de 4 semanas",
        providerUsed: "Rules",
        exercisesJson: """[{"exerciseId":"00000000-0000-0000-0000-000000000001","sets":3,"reps":8}]""");

    private AppDbContext CreateContext(Guid pfId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(user => user.PfId).Returns(pfId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        return new AppDbContext(options, currentUser.Object);
    }
}
