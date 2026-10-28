using API.Controllers;
using Application.Common.PlanDrafts;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.DiscardPlanDraft;
using Application.UseCases.PlanDrafts.GetPlanDraft;
using Application.UseCases.PlanDrafts.UpdatePlanDraft;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

namespace Asistente.Tests.Security;

// KAN-430 y KAN-410: un PF no ve ni toca los borradores del Asistente de otro PF. Se prueba contra Postgres real
// con el filtro global por PF y a través del controlador PlanDraftsController (GET, PUT y POST /discard -> 404).
[Trait("Category", "Isolation")]
[Trait("Category", "RequiresDocker")]
public sealed class RoutineDraftIsolationTests : IAsyncLifetime
{
    private static readonly Guid ExerciseId = Guid.Parse("00000000-0000-0000-0000-000000000001");

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

    [Fact]
    public async Task Given_PlanDraftOfPfB_When_PfAGetsUpdatesOrDiscardsViaController_Then_ThrowsNotFound404AndDraftRemainsIntact()
    {
        // arrange: PF-B crea un borrador de planificación (plan_drafts)
        var planDraftId = await SavePlanDraftAsPfBAsync();

        await using var contextPfA = CreateContext(_pfAId);
        var controllerPfA = CreateControllerForContext(contextPfA);

        var maliciousUpdate = new UpdatePlanDraftRequestDto(
            Name: "Intento de modificación cruzada",
            Objective: "Alterar borrador de otro PF",
            JustificationSummary: "No debería aplicarse",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión intrusa",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(ExerciseId, Order: 1, Sets: 3, Reps: 5, TargetPercentage: 50m)
                            ])
                    ])
            ]);

        // act & assert: PF-A recibe 404 (NotFoundException) en GET, PUT y POST /discard
        await Assert.ThrowsAsync<NotFoundException>(() =>
            controllerPfA.GetById(planDraftId, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            controllerPfA.Update(planDraftId, maliciousUpdate, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            controllerPfA.Discard(planDraftId, CancellationToken.None));

        // y el borrador de PF-B permanece intacto en la base de datos
        await using var contextPfB = CreateContext(_pfBId);
        var intactDraft = await new PlanDraftRepository(contextPfB).GetByIdAsNoTrackingAsync(planDraftId);
        Assert.NotNull(intactDraft);
        Assert.Equal(PlanDraftStatus.Draft, intactDraft.Status);
        var intactContent = PlanDraftMapper.DeserializeContent(intactDraft.Content);
        Assert.Equal("Plan Original de PF-B", intactContent.Name);
        Assert.Equal(80m, intactContent.Weeks[0].Sessions[0].Exercises[0].TargetPercentage);
    }

    private async Task<Guid> SaveDraftAsPfBAsync()
    {
        await using var contextPfB = CreateContext(_pfBId);
        var draft = NewDraft("Objetivo de PF-B");
        await new RoutineDraftRepository(contextPfB).AddAsync(draft);
        await contextPfB.SaveChangesAsync();
        return draft.Id;
    }

    private async Task<Guid> SavePlanDraftAsPfBAsync()
    {
        await using var contextPfB = CreateContext(_pfBId);
        var content = new PlanDraftContentDto(
            Name: "Plan Original de PF-B",
            Objective: "Fuerza de piernas",
            JustificationSummary: "Progresión lineal de 4 semanas",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión 1",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(ExerciseId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 80m)
                            ])
                    ])
            ]);

        var planDraft = PlanDraft.Create(
            categoryId: null,
            athleteId: _athleteOfPfB,
            goal: "Fuerza de piernas",
            startDate: new DateOnly(2026, 11, 2),
            content: PlanDraftMapper.SerializeContent(content),
            provider: "Rules");

        await new PlanDraftRepository(contextPfB).AddAsync(planDraft);
        await contextPfB.SaveChangesAsync();
        return planDraft.Id;
    }

    private RoutineDraft NewDraft(string goal) => RoutineDraft.Create(
        athleteId: _athleteOfPfB,
        goal: goal,
        proposedTitle: "Fuerza base",
        justificationSummary: "Progresión lineal de 4 semanas",
        providerUsed: "Rules",
        exercisesJson: """[{"exerciseId":"00000000-0000-0000-0000-000000000001","sets":3,"reps":8}]""");

    private static PlanDraftsController CreateControllerForContext(AppDbContext context)
    {
        var draftRepository = new PlanDraftRepository(context);
        var decisionRepository = new PfDecisionRepository(context);
        var unitOfWork = new UnitOfWork(context);

        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock
            .Setup(c => c.FindByIdAsync(ExerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogExerciseDto(ExerciseId, "Sentadilla trasera con barra", "Piernas"));

        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new PlanDraftsController(
            new GetPlanDraftQueryHandler(draftRepository),
            new UpdatePlanDraftCommandHandler(
                draftRepository,
                decisionRepository,
                catalogMock.Object,
                new MatchDayLoadValidator(),
                calendarMock.Object,
                unitOfWork),
            new DiscardPlanDraftCommandHandler(draftRepository, unitOfWork));
    }

    private AppDbContext CreateContext(Guid pfId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(user => user.PfId).Returns(pfId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        return new AppDbContext(options, currentUser.Object);
    }
}
