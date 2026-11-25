namespace Asistente.Tests;

using API.Controllers;
using Application.Common.PlanDrafts;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Application.UseCases.PlanDrafts.UpdatePlanDraft;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas de resiliencia y degradación elegante ante caídas de servicios externos (#191 · KAN-486 · Épica E40).
/// Valida que la aplicación continúe funcionando con capacidades reducidas si un microservicio secundario (Estructura) falla.
/// </summary>
public sealed class PlanDraftResilienceTests
{
    private static readonly Guid CategoryId = Guid.Parse("77777777-8888-9999-aaaa-bbbbbbbbbbbb");
    private static readonly Guid ExerciseId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000");

    [Fact]
    public async Task Given_StructureDown_When_PfRequestsPlanDraft_Then_PlanIsGeneratedIndicatingFixtureSyncFailed()
    {
        // Arrange (DADO el servicio de Estructura caído)
        var startDate = new DateOnly(2026, 11, 2);
        var request = new CreatePlanDraftRequestDto(
            CategoryId: CategoryId,
            Goal: "Fuerza y Potencia Pretemporada",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: startDate
        );

        var sampleContent = new PlanDraftContentDto(
            Name: "Planificación Pretemporada",
            Objective: "Fuerza y Potencia Pretemporada",
            JustificationSummary: "Progresión de 4 semanas",
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
                                new PlanDraftExerciseDto(ExerciseId, Order: 1, Sets: 3, Reps: 10, TargetPercentage: 70m)
                            ]
                        )
                    ]
                )
            ]
        );

        // Estructura arroja excepción de indisponibilidad (caída total de servicio)
        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureServiceUnavailableException("El microservicio de Estructura no se encuentra disponible."));

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(
                It.IsAny<CreatePlanDraftRequestDto>(),
                It.Is<IReadOnlyList<DateOnly>?>(m => m == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((sampleContent, "Reglas"));

        PlanDraft? savedDraft = null;
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()))
            .Callback<PlanDraft, CancellationToken>((d, _) => savedDraft = d)
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            draftRepoMock.Object,
            uowMock.Object,
            calendarMock.Object,
            logger: NullLogger<CreatePlanDraftCommandHandler>.Instance
        );

        // Act (CUANDO el PF solicita una planificación al Asistente)
        var response = await handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None);

        // Assert (ENTONCES la planificación se genera igualmente indicando que no se pudo sincronizar el fixture)
        Assert.NotNull(response);
        Assert.NotNull(savedDraft);
        Assert.Equal("Draft", response.Status);
        Assert.Equal("Planificación Pretemporada", response.Name);
        Assert.NotEmpty(response.Weeks);

        // Se verifica que la advertencia informe con claridad que no se pudo sincronizar el fixture
        Assert.NotNull(response.Warnings);
        Assert.Contains(response.Warnings, w => w.Contains("no se pudo sincronizar el fixture", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Given_StructureDown_When_PfRequestsPlanDraftViaController_Then_Returns201CreatedWithFixtureWarning()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            CategoryId: CategoryId,
            Goal: "Hipertrofia Muscular",
            Weeks: 2,
            DaysPerWeek: 3
        );

        var sampleContent = new PlanDraftContentDto(
            Name: "Plan Hipertrofia",
            Objective: "Hipertrofia Muscular",
            JustificationSummary: "3x10 general",
            Weeks: []
        );

        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<IReadOnlyList<DateOnly>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((sampleContent, "Ollama"));

        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            draftRepoMock.Object,
            uowMock.Object,
            calendarMock.Object
        );

        var controller = new PlanDraftsController(
            getPlanDraftHandler: null!,
            updatePlanDraftHandler: null!,
            approvePlanDraftHandler: null!,
            discardPlanDraftHandler: null!,
            createPlanDraftHandler: handler);

        // Act
        var actionResult = await controller.Create(request, CancellationToken.None);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        Assert.Equal(201, createdResult.StatusCode);

        var response = Assert.IsType<PlanDraftResponseDto>(createdResult.Value);
        Assert.NotNull(response.Warnings);
        Assert.Contains(response.Warnings, w => w.Contains("no se pudo sincronizar el fixture", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Given_StructureDown_When_PfUpdatesPlanDraft_Then_PlanIsUpdatedWithFixtureWarning()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var initialContent = """
            {
                "name": "Borrador Original",
                "objective": "Objetivo",
                "justificationSummary": "Resumen",
                "weeks": [
                    {
                        "weekNumber": 1,
                        "sessions": [
                            {
                                "name": "Sesión 1",
                                "dayOfWeek": "Monday",
                                "order": 1,
                                "coachNotes": null,
                                "exercises": [
                                    { "exerciseId": "cccccccc-dddd-eeee-ffff-000000000000", "order": 1, "sets": 3, "reps": 8, "targetPercentage": 75 }
                                ]
                            }
                        ]
                    }
                ]
            }
            """;

        var draft = PlanDraft.Create(
            categoryId: CategoryId,
            athleteId: null,
            goal: "Objetivo",
            startDate: new DateOnly(2026, 11, 2),
            content: initialContent,
            provider: "Reglas"
        );

        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock.Setup(r => r.GetByIdAsync(draftId, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var pfDecisionRepoMock = new Mock<IPfDecisionRepository>();
        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock.Setup(c => c.FindByIdAsync(ExerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogExerciseDto(ExerciseId, "Press de Banca", "Pecho", "Barra"));

        var matchDayLoadValidator = new MatchDayLoadValidator();

        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureServiceUnavailableException("Estructura caído."));

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var updateHandler = new UpdatePlanDraftCommandHandler(
            draftRepoMock.Object,
            pfDecisionRepoMock.Object,
            catalogMock.Object,
            matchDayLoadValidator,
            calendarMock.Object,
            uowMock.Object
        );

        var updateRequest = new UpdatePlanDraftRequestDto(
            Name: "Borrador Editado",
            Objective: "Objetivo Editado",
            JustificationSummary: "Resumen Editado",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión 1 Editada",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(ExerciseId, Order: 1, Sets: 4, Reps: 10, TargetPercentage: 80m)
                            ]
                        )
                    ]
                )
            ]
        );

        // Act
        var response = await updateHandler.HandleAsync(new UpdatePlanDraftCommand(draftId, updateRequest), CancellationToken.None);

        // Assert: No debe arrojar excepción 503, sino actualizar el borrador e informar en warnings
        Assert.NotNull(response);
        Assert.Equal("Borrador Editado", response.Name);
        Assert.NotNull(response.Warnings);
        Assert.Contains(response.Warnings, w => w.Contains("no se pudo sincronizar el fixture", StringComparison.OrdinalIgnoreCase));
    }
}
