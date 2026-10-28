using System.Reflection;
using API.Controllers;
using Application.Common.PlanDrafts;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.DiscardPlanDraft;
using Application.UseCases.PlanDrafts.GetPlanDraft;
using Application.UseCases.PlanDrafts.UpdatePlanDraft;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias para la edición, validación, memoria de decisiones del PF y descarte de borradores (#115 · KAN-410).
/// </summary>
public sealed class PlanDraftEditAndDiscardTests
{
    private static readonly Guid CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AthleteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SquatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BenchPressId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid HipThrustId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateOnly StartMonday = new(2026, 11, 2); // Lunes 2/11/2026

    [Fact]
    public async Task Given_AiPlanDraft_When_PfChangesPercentageFrom80To75AndSaves_Then_DraftKeepsNewPercentageAndIntactStructureWithoutCreatingPfDecision()
    {
        // Arrange
        var draft = CreateSampleDraft(forAthlete: true);
        var (handler, draftRepoMock, decisionRepoMock, _, _, uowMock) = CreateUpdateHandler(draft);

        var updatedRequest = new UpdatePlanDraftRequestDto(
            Name: "Mesociclo de Fuerza Base",
            Objective: "Subir fuerza máxima en sentadilla",
            JustificationSummary: "Progresión lineal controlada",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Día 1 - Tren Inferior",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: "Cuidar técnica",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(SquatId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 75m),
                                new PlanDraftExerciseDto(BenchPressId, Order: 2, Sets: 3, Reps: 8, TargetPercentage: 70m)
                            ])
                    ])
            ]);

        // Act
        var response = await handler.HandleAsync(new UpdatePlanDraftCommand(draft.Id, updatedRequest));

        // Assert: El porcentaje cambió de 80% a 75% y el resto de la estructura sigue intacta
        Assert.Equal(draft.Id, response.Id);
        Assert.Equal("Draft", response.Status);
        var week = Assert.Single(response.Weeks);
        Assert.Equal(1, week.WeekNumber);
        var session = Assert.Single(week.Sessions);
        Assert.Equal("Día 1 - Tren Inferior", session.Name);
        Assert.Equal("Cuidar técnica", session.CoachNotes);
        Assert.Equal(2, session.Exercises.Count);
        Assert.Equal(75m, session.Exercises[0].TargetPercentage);
        Assert.Equal(4, session.Exercises[0].Sets);
        Assert.Equal(6, session.Exercises[0].Reps);
        Assert.Equal(BenchPressId, session.Exercises[1].ExerciseId);
        Assert.Equal(70m, session.Exercises[1].TargetPercentage);

        // No se debe guardar decisión en pf_decisions porque solo cambió el porcentaje
        decisionRepoMock.Verify(
            r => r.AddRangeAsync(It.IsAny<IEnumerable<PfDecision>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ApprovedPlanDraft_When_PfAttemptsToEdit_Then_ThrowsConflictException409()
    {
        // Arrange
        var draft = CreateSampleDraft();
        draft.Approve(Guid.NewGuid());
        var (handler, _, _, _, _, uowMock) = CreateUpdateHandler(draft);

        var request = BuildValidUpdateRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            handler.HandleAsync(new UpdatePlanDraftCommand(draft.Id, request)));

        Assert.Equal("El borrador ya fue aprobado.", ex.Message);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_DiscardedPlanDraft_When_PfAttemptsToEdit_Then_ThrowsConflictException409()
    {
        // Arrange
        var draft = CreateSampleDraft();
        draft.Discard();
        var (handler, _, _, _, _, uowMock) = CreateUpdateHandler(draft);

        var request = BuildValidUpdateRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            handler.HandleAsync(new UpdatePlanDraftCommand(draft.Id, request)));

        Assert.Equal("El borrador ya fue descartado.", ex.Message);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_DraftOfAnotherPfOrNonExistent_When_PfAttemptsToEdit_Then_ThrowsNotFoundException404()
    {
        // Arrange: el repositorio filtrado por PfId devuelve null
        var (handler, _, _, _, _, uowMock) = CreateUpdateHandler(existingDraft: null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new UpdatePlanDraftCommand(Guid.NewGuid(), BuildValidUpdateRequest())));

        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_MatchOnSaturday_When_PfPlacesSessionOnSaturdayAndThursday_Then_RemovesSaturdaySessionAndCapsThursdayTo75Percent()
    {
        // Arrange: StartMonday = Lunes 2/11/2026. Jueves (D-2) = 5/11/2026. Sábado (D) = 7/11/2026.
        var saturdayMatch = new DateOnly(2026, 11, 7);
        var draft = CreateSampleDraft(forAthlete: false);
        var (handler, _, _, _, _, _) = CreateUpdateHandler(draft, matchDates: [saturdayMatch]);

        var request = new UpdatePlanDraftRequestDto(
            Name: "Mesociclo Competitivo",
            Objective: "Control de cargas en semana de partido",
            JustificationSummary: "Respeto del calendario competitivo",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Jueves Fuerza",
                            DayOfWeek: DayOfWeek.Thursday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(SquatId, Order: 1, Sets: 4, Reps: 5, TargetPercentage: 85m)
                            ]),
                        new PlanDraftSessionDto(
                            Name: "Sábado Fuerza",
                            DayOfWeek: DayOfWeek.Saturday,
                            Order: 2,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(BenchPressId, Order: 1, Sets: 3, Reps: 8, TargetPercentage: 70m)
                            ])
                    ])
            ]);

        // Act
        var result = await handler.HandleAsync(new UpdatePlanDraftCommand(draft.Id, request));

        // Assert: La sesión del sábado se quitó y el jueves bajó de 85% a 75%
        var week = Assert.Single(result.Weeks);
        var onlySession = Assert.Single(week.Sessions);
        Assert.Equal("Jueves Fuerza", onlySession.Name);
        Assert.Equal(75m, onlySession.Exercises[0].TargetPercentage);
        Assert.NotNull(result.Warnings);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("85 % → 75 %"));
        Assert.Contains(result.Warnings, w => w.Contains("Se quitó la sesión 'Sábado Fuerza'"));
    }

    [Fact]
    public async Task Given_AiPlanDraft_When_PfReplacesOneExerciseAndRemovesAnother_Then_SavesModifiedAndRejectedPfDecisions()
    {
        // Arrange: Borrador original tiene SquatId (orden 1) y BenchPressId (orden 2)
        var draft = CreateSampleDraft(forAthlete: true);
        IReadOnlyList<PfDecision>? capturedDecisions = null;
        var (handler, _, decisionRepoMock, _, _, uowMock) = CreateUpdateHandler(
            draft,
            onDecisionsAdded: d => capturedDecisions = d);

        // El PF reemplaza SquatId (orden 1) por HipThrustId (orden 1, al 70%) y quita BenchPressId (orden 2)
        var request = new UpdatePlanDraftRequestDto(
            Name: "Mesociclo de Fuerza Base",
            Objective: "Subir fuerza máxima en cadera",
            JustificationSummary: "Priorizar empuje de cadera",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Día 1 - Tren Inferior",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(HipThrustId, Order: 1, Sets: 4, Reps: 8, TargetPercentage: 70m)
                            ])
                    ])
            ]);

        // Act
        await handler.HandleAsync(new UpdatePlanDraftCommand(draft.Id, request));

        // Assert
        Assert.NotNull(capturedDecisions);
        Assert.Equal(2, capturedDecisions.Count);

        var modified = Assert.Single(capturedDecisions, d => d.Decision == PfDecisionType.Modified);
        Assert.Equal(AthleteId, modified.AthleteId);
        Assert.Equal(PfDecisionSource.PlanDraft, modified.Source);
        Assert.Equal(draft.Id, modified.SourceId);
        Assert.Equal(SquatId, modified.SuggestedExerciseId);
        Assert.Equal(HipThrustId, modified.FinalExerciseId);
        Assert.Equal(70m, modified.FinalPercentage);

        var rejected = Assert.Single(capturedDecisions, d => d.Decision == PfDecisionType.Rejected);
        Assert.Equal(BenchPressId, rejected.SuggestedExerciseId);
        Assert.Null(rejected.FinalExerciseId);

        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_MatchOnSaturday_When_PfRaisesFridayLoadAbove60Percent_Then_CapsTo60PercentAndReturnsWarning()
    {
        // Arrange: StartMonday = Lunes 2/11/2026. Sábado de la semana 1 = 7/11/2026. Viernes (D-1) = 6/11/2026.
        var saturdayMatch = new DateOnly(2026, 11, 7);
        var draft = CreateSampleDraft(forAthlete: false);
        var (handler, _, _, _, _, _) = CreateUpdateHandler(draft, matchDates: [saturdayMatch]);

        var request = new UpdatePlanDraftRequestDto(
            Name: "Mesociclo Competitivo",
            Objective: "Mantener fuerza en competencia",
            JustificationSummary: "Control de cargas",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Viernes Pre-Partido",
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 1,
                            CoachNotes: "Activación",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(SquatId, Order: 1, Sets: 3, Reps: 5, TargetPercentage: 85m)
                            ])
                    ])
            ]);

        // Act
        var result = await handler.HandleAsync(new UpdatePlanDraftCommand(draft.Id, request));

        // Assert: La carga de 85% en D-1 se corrige a 60%, se anota en coachNotes y se informa en Warnings
        var session = Assert.Single(Assert.Single(result.Weeks).Sessions);
        var exercise = Assert.Single(session.Exercises);
        Assert.Equal(60m, exercise.TargetPercentage);
        Assert.NotNull(session.CoachNotes);
        Assert.Contains("85 % → 60 %", session.CoachNotes);
        Assert.NotNull(result.Warnings);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("sábado 7/11", warning);
        Assert.Contains("85 % → 60 %", warning);
    }

    [Fact]
    public async Task Given_UncatalogedExerciseId_When_PfUpdatesDraft_Then_ThrowsDomainException400()
    {
        // Arrange
        var draft = CreateSampleDraft();
        var (handler, _, _, _, _, _) = CreateUpdateHandler(draft);
        var unknownExerciseId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var request = new UpdatePlanDraftRequestDto(
            Name: "Mesociclo Inválido",
            Objective: "Probar ejercicio inexistente",
            JustificationSummary: "Validación de catálogo",
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
                                new PlanDraftExerciseDto(unknownExerciseId, Order: 1, Sets: 3, Reps: 8, TargetPercentage: 70m)
                            ])
                    ])
            ]);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new UpdatePlanDraftCommand(draft.Id, request)));

        Assert.Contains("no existe en el catálogo oficial", ex.Message);
    }

    [Theory]
    [InlineData(0, 8, 70)]
    [InlineData(11, 8, 70)]
    [InlineData(3, 0, 70)]
    [InlineData(3, 31, 70)]
    [InlineData(3, 8, -5)]
    [InlineData(3, 8, 105)]
    public async Task Given_OutOfRangeSetsRepsOrPercentage_When_PfUpdatesDraft_Then_ThrowsDomainException400(
        int sets,
        int reps,
        decimal percentage)
    {
        // Arrange
        var draft = CreateSampleDraft();
        var (handler, _, _, _, _, _) = CreateUpdateHandler(draft);

        var request = new UpdatePlanDraftRequestDto(
            Name: "Mesociclo Rangos",
            Objective: "Validar topes de esquema",
            JustificationSummary: "Control de rangos",
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
                                new PlanDraftExerciseDto(SquatId, Order: 1, Sets: sets, Reps: reps, TargetPercentage: percentage)
                            ])
                    ])
            ]);

        // Act & Assert
        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new UpdatePlanDraftCommand(draft.Id, request)));
    }

    [Fact]
    public async Task Given_DraftInDraftState_When_PfDiscardsIt_Then_TransitionsToDiscardedAndSaves()
    {
        // Arrange
        var draft = CreateSampleDraft();
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock
            .Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DiscardPlanDraftCommandHandler(draftRepoMock.Object, uowMock.Object);

        // Act
        var result = await handler.HandleAsync(new DiscardPlanDraftCommand(draft.Id));

        // Assert
        Assert.Equal("Discarded", result.Status);
        Assert.Equal(PlanDraftStatus.Discarded, draft.Status);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Given_PlanDraftsController_When_InspectingAttributes_Then_RequiresPreparadorFisicoRoleAndEnglishRoute()
    {
        var authAttribute = typeof(PlanDraftsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttribute);
        Assert.Equal("PreparadorFisico", authAttribute.Roles);

        var routeAttribute = typeof(PlanDraftsController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttribute);
        Assert.Equal("api/v1/assistant/plan-drafts", routeAttribute.Template);
    }

    private static PlanDraft CreateSampleDraft(bool forAthlete = false)
    {
        var initialContent = new PlanDraftContentDto(
            Name: "Mesociclo de Fuerza Base",
            Objective: "Subir fuerza máxima en sentadilla",
            JustificationSummary: "Progresión lineal controlada",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Día 1 - Tren Inferior",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: "Cuidar técnica",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(SquatId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 80m),
                                new PlanDraftExerciseDto(BenchPressId, Order: 2, Sets: 3, Reps: 8, TargetPercentage: 70m)
                            ])
                    ])
            ]);

        return PlanDraft.Create(
            categoryId: forAthlete ? null : CategoryId,
            athleteId: forAthlete ? AthleteId : null,
            goal: "Fuerza máxima de piernas",
            startDate: StartMonday,
            content: PlanDraftMapper.SerializeContent(initialContent),
            provider: "Ollama");
    }

    private static UpdatePlanDraftRequestDto BuildValidUpdateRequest() =>
        new(
            Name: "Mesociclo de Fuerza Base",
            Objective: "Subir fuerza máxima en sentadilla",
            JustificationSummary: "Progresión lineal controlada",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Día 1 - Tren Inferior",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: "Cuidar técnica",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(SquatId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 75m)
                            ])
                    ])
            ]);

    private static (
        UpdatePlanDraftCommandHandler Handler,
        Mock<IPlanDraftRepository> DraftRepoMock,
        Mock<IPfDecisionRepository> DecisionRepoMock,
        Mock<IExerciseCatalogService> CatalogMock,
        Mock<IStructureCalendarClient> CalendarMock,
        Mock<IUnitOfWork> UowMock)
        CreateUpdateHandler(
            PlanDraft? existingDraft,
            IReadOnlyList<DateOnly>? matchDates = null,
            Action<IReadOnlyList<PfDecision>>? onDecisionsAdded = null)
    {
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        if (existingDraft is not null)
        {
            draftRepoMock
                .Setup(r => r.GetByIdAsync(existingDraft.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingDraft);
        }

        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        decisionRepoMock
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<PfDecision>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<PfDecision>, CancellationToken>((d, _) => onDecisionsAdded?.Invoke(d.ToList()))
            .Returns(Task.CompletedTask);

        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock
            .Setup(c => c.FindByIdAsync(SquatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogExerciseDto(SquatId, "Sentadilla trasera con barra", "Cuádriceps"));
        catalogMock
            .Setup(c => c.FindByIdAsync(BenchPressId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogExerciseDto(BenchPressId, "Press de banca plano con barra", "Pecho"));
        catalogMock
            .Setup(c => c.FindByIdAsync(HipThrustId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogExerciseDto(HipThrustId, "Empuje de cadera (Hip Thrust)", "Glúteos"));

        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchDates ?? []);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdatePlanDraftCommandHandler(
            draftRepoMock.Object,
            decisionRepoMock.Object,
            catalogMock.Object,
            new MatchDayLoadValidator(),
            calendarMock.Object,
            uowMock.Object);

        return (handler, draftRepoMock, decisionRepoMock, catalogMock, calendarMock, uowMock);
    }
}
