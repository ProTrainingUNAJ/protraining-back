namespace Asistente.Tests;

using System.Reflection;
using API.Controllers;
using Application.Common.PlanDrafts;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Application.UseCases.PlanDrafts.DiscardPlanDraft;
using Application.UseCases.PlanDrafts.GetPlanDraft;
using Application.UseCases.PlanDrafts.UpdatePlanDraft;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias de generación asistida por IA (KAN-381 / KAN-383),
/// validación de esquemas, exclusión mutua atleta/categoría y aislamiento multi-tenant estricto.
/// </summary>
public sealed class PlanDraftCreationAndIsolationTests
{
    private static readonly Guid AthleteId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid CategoryId = Guid.Parse("66666666-7777-8888-9999-000000000000");
    private static readonly Guid SquatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BenchPressId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Given_ValidRequest_When_CreatingPlanDraft_Then_Returns201CreatedAndPersists()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            AthleteId: AthleteId,
            CategoryId: null,
            Goal: "Fuerza y Potencia en Sentadilla",
            Weeks: 4,
            DaysPerWeek: 3
        );

        var sampleContent = new PlanDraftContentDto(
            Name: "Planificación Fuerza y Potencia en Sentadilla",
            Objective: "Fuerza y Potencia en Sentadilla",
            JustificationSummary: "Sobrecarga progresiva 3x8 al 70%",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión 1 - Fuerza Base",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: "Enfoque técnico",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(SquatId, Order: 1, Sets: 3, Reps: 8, TargetPercentage: 70m)
                            ]
                        )
                    ]
                )
            ]
        );

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((sampleContent, "Reglas"));

        PlanDraft? savedDraft = null;
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()))
            .Callback<PlanDraft, CancellationToken>((d, _) => savedDraft = d)
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreatePlanDraftCommandHandler(aiServiceMock.Object, draftRepoMock.Object, uowMock.Object);
        var controller = new PlanDraftsController(
            getPlanDraftHandler: null!,
            updatePlanDraftHandler: null!,
            approvePlanDraftHandler: null!,
            discardPlanDraftHandler: null!,
            createPlanDraftHandler: handler);

        // Act
        var result = await controller.Create(request, CancellationToken.None);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(201, createdResult.StatusCode);

        var response = Assert.IsType<PlanDraftResponseDto>(createdResult.Value);
        Assert.NotNull(response);
        Assert.Equal(AthleteId, response.AthleteId);
        Assert.Null(response.CategoryId);
        Assert.Equal("Fuerza y Potencia en Sentadilla", response.Goal);
        Assert.Equal("Reglas", response.Provider);
        Assert.Equal("Draft", response.Status);
        Assert.Single(response.Weeks);

        Assert.NotNull(savedDraft);
        Assert.Equal(response.Id, savedDraft.Id);
        Assert.Equal(AthleteId, savedDraft.AthleteId);
        Assert.Equal(PlanDraftStatus.Draft, savedDraft.Status);
        Assert.Equal("Reglas", savedDraft.Provider);

        draftRepoMock.Verify(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()), Times.Once);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_MutualExclusionViolation_BothCategoryAndAthlete_When_CreatingPlanDraft_Then_ThrowsDomainException400()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            AthleteId: AthleteId,
            CategoryId: CategoryId,
            Goal: "Objetivo conflictivo"
        );

        var aiServiceMock = new Mock<IAiService>();
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var handler = new CreatePlanDraftCommandHandler(aiServiceMock.Object, draftRepoMock.Object, uowMock.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None));

        Assert.Contains("Debés indicar una categoría o un atleta, pero no ambos a la vez.", ex.Message);
        draftRepoMock.Verify(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()), Times.Never);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_MutualExclusionViolation_NeitherCategoryNorAthlete_When_CreatingPlanDraft_Then_ThrowsDomainException400()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            AthleteId: null,
            CategoryId: null,
            Goal: "Sin destinatario"
        );

        var aiServiceMock = new Mock<IAiService>();
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var handler = new CreatePlanDraftCommandHandler(aiServiceMock.Object, draftRepoMock.Object, uowMock.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None));

        Assert.Contains("Debés indicar una categoría o un atleta, pero no ambos a la vez.", ex.Message);
        draftRepoMock.Verify(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Given_EmptyGoal_When_CreatingPlanDraft_Then_ThrowsDomainException400(string? invalidGoal)
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            AthleteId: AthleteId,
            CategoryId: null,
            Goal: invalidGoal!
        );

        var aiServiceMock = new Mock<IAiService>();
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var handler = new CreatePlanDraftCommandHandler(aiServiceMock.Object, draftRepoMock.Object, uowMock.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None));

        Assert.Contains("El objetivo de entrenamiento es obligatorio.", ex.Message);
    }

    [Fact]
    public async Task Given_GoalExceedingMaxLength_When_CreatingPlanDraft_Then_ThrowsDomainException400()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            AthleteId: AthleteId,
            CategoryId: null,
            Goal: new string('A', 501)
        );

        var aiServiceMock = new Mock<IAiService>();
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var handler = new CreatePlanDraftCommandHandler(aiServiceMock.Object, draftRepoMock.Object, uowMock.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None));

        Assert.Contains("El objetivo no puede superar los 500 caracteres.", ex.Message);
    }

    [Fact]
    public async Task Given_AllProvidersFail_When_CreatingPlanDraft_Then_ThrowsAiServiceUnavailableExceptionAndDoesNotPersist()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            AthleteId: AthleteId,
            CategoryId: null,
            Goal: "Plan con IA no disponible"
        );

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiServiceUnavailableException("El servicio de IA no está disponible en este momento."));

        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var handler = new CreatePlanDraftCommandHandler(aiServiceMock.Object, draftRepoMock.Object, uowMock.Object);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() =>
            handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None));

        Assert.Contains("El servicio de IA no está disponible", ex.Message);
        draftRepoMock.Verify(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()), Times.Never);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_DraftBelongsToPfB_When_PfARequestsById_Then_Returns404NotFound()
    {
        // Arrange: Repositorio aplica filtro global de tenant (WHERE pf_id = @PfA). Para un ID de PfB, retorna null.
        var targetDraftId = Guid.NewGuid();
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock
            .Setup(r => r.GetByIdAsNoTrackingAsync(targetDraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanDraft?)null); // Filtrado por tenant

        var handler = new GetPlanDraftQueryHandler(draftRepoMock.Object);

        // Act & Assert: Debe arrojar NotFoundException (HTTP 404), nunca 403 para no filtrar existencia
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetPlanDraftQuery(targetDraftId), CancellationToken.None));

        Assert.Contains("El borrador solicitado no existe.", ex.Message);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_DraftBelongsToPfB_When_PfAAttemptsToUpdate_Then_Returns404NotFound()
    {
        // Arrange: Repositorio filtra por tenant y no encuentra el recurso
        var targetDraftId = Guid.NewGuid();
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock
            .Setup(r => r.GetByIdAsync(targetDraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanDraft?)null);

        var updateRequest = new UpdatePlanDraftRequestDto(
            Name: "Plan Alterado",
            Objective: "Objetivo",
            JustificationSummary: "Justificación",
            Weeks: []
        );

        var handler = new UpdatePlanDraftCommandHandler(
            draftRepoMock.Object,
            Mock.Of<IPfDecisionRepository>(),
            Mock.Of<IExerciseCatalogService>(),
            new MatchDayLoadValidator(),
            Mock.Of<IStructureCalendarClient>(),
            Mock.Of<IUnitOfWork>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new UpdatePlanDraftCommand(targetDraftId, updateRequest), CancellationToken.None));

        Assert.Contains("El borrador solicitado no existe.", ex.Message);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_DraftBelongsToPfB_When_PfAAttemptsToDiscard_Then_Returns404NotFound()
    {
        // Arrange
        var targetDraftId = Guid.NewGuid();
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock
            .Setup(r => r.GetByIdAsync(targetDraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanDraft?)null);

        var handler = new DiscardPlanDraftCommandHandler(draftRepoMock.Object, Mock.Of<IUnitOfWork>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new DiscardPlanDraftCommand(targetDraftId), CancellationToken.None));

        Assert.Contains("El borrador solicitado no existe.", ex.Message);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public void Given_BaseEntity_When_InspectingProperties_Then_PfIdAndIdHavePrivateSettersForTenantSecurity()
    {
        // Arrange & Act
        var pfIdProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId));
        var idProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id));
        var createdAtProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt));
        var deletedAtProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.DeletedAt));

        // Assert: Ninguna de las claves multi-tenant puede tener setter público desde fuera del contexto de persistencia EF Core
        Assert.NotNull(pfIdProp);
        Assert.Null(pfIdProp.GetSetMethod(nonPublic: false));
        Assert.True(pfIdProp.GetSetMethod(nonPublic: true)?.IsPrivate);

        Assert.NotNull(idProp);
        Assert.Null(idProp.GetSetMethod(nonPublic: false));
        Assert.True(idProp.GetSetMethod(nonPublic: true)?.IsPrivate);

        Assert.NotNull(createdAtProp);
        Assert.Null(createdAtProp.GetSetMethod(nonPublic: false));
        Assert.True(createdAtProp.GetSetMethod(nonPublic: true)?.IsPrivate);

        Assert.NotNull(deletedAtProp);
        Assert.Null(deletedAtProp.GetSetMethod(nonPublic: false));
        Assert.True(deletedAtProp.GetSetMethod(nonPublic: true)?.IsPrivate);
    }

    [Fact]
    public void Given_PlanDraftAiParser_When_ParsingMarkdownFenceResponse_Then_CleansFencesAndDeserializes()
    {
        // Arrange
        var markdownResponse = """
            ```json
            {
              "name": "Mesociclo de Hipertrofia",
              "objective": "Aumento de masa muscular",
              "justificationSummary": "Volumen acumulativo semanal",
              "weeks": [
                {
                  "weekNumber": 1,
                  "sessions": [
                    {
                      "name": "Día 1 - Empuje",
                      "dayOfWeek": 1,
                      "order": 1,
                      "coachNotes": "RPE 8",
                      "exercises": [
                        {
                          "exerciseId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                          "order": 1,
                          "sets": 4,
                          "reps": 10,
                          "targetPercentage": 72.5
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            ```
            """;

        // Act
        var (isValid, content, errorMessage, isSyntaxError) = PlanDraftAiParser.Parse(markdownResponse);

        // Assert
        Assert.True(isValid);
        Assert.False(isSyntaxError);
        Assert.Null(errorMessage);
        Assert.NotNull(content);
        Assert.Equal("Mesociclo de Hipertrofia", content.Name);
        Assert.Single(content.Weeks);
        Assert.Single(content.Weeks[0].Sessions);
        Assert.Equal(SquatId, content.Weeks[0].Sessions[0].Exercises[0].ExerciseId);
    }

    [Fact]
    public async Task Given_PlanDraftContentValidator_When_ExerciseIdNotFoundInCatalog_Then_ThrowsDomainException()
    {
        // Arrange: La IA inventa / alucina un ejercicio no existente
        var hallucinatedId = Guid.NewGuid();
        var content = new PlanDraftContentDto(
            Name: "Plan con ejercicio inventado",
            Objective: "Objetivo válido",
            JustificationSummary: "Justificación válida",
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
                            CoachNotes: "Nota",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(hallucinatedId, Order: 1, Sets: 3, Reps: 10, TargetPercentage: 70m)
                            ]
                        )
                    ]
                )
            ]
        );

        var catalogServiceMock = new Mock<IExerciseCatalogService>();
        catalogServiceMock
            .Setup(c => c.FindByIdAsync(hallucinatedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogExerciseDto?)null); // Ejercicio no existe en catálogo

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            PlanDraftContentValidator.ValidateAsync(content, catalogServiceMock.Object, CancellationToken.None));

        Assert.Contains($"El ejercicio con identificador '{hallucinatedId}' no existe en el catálogo oficial.", ex.Message);
    }

    [Fact]
    public void Given_NextMondayCalculation_When_Executed_Then_AlwaysCalculatesNextMondayInArgentina()
    {
        // Caso 1: Miércoles 28 de Octubre de 2026 -> Próximo lunes es 2 de Noviembre de 2026
        var wednesday = new DateTime(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc);
        var nextMondayFromWed = CreatePlanDraftCommandHandler.CalculateNextMondayInArgentina(wednesday);
        Assert.Equal(DayOfWeek.Monday, nextMondayFromWed.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 11, 2), nextMondayFromWed);

        // Caso 2: Domingo 1 de Noviembre de 2026 -> Próximo lunes es 2 de Noviembre de 2026
        var sunday = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var nextMondayFromSun = CreatePlanDraftCommandHandler.CalculateNextMondayInArgentina(sunday);
        Assert.Equal(DayOfWeek.Monday, nextMondayFromSun.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 11, 2), nextMondayFromSun);

        // Caso 3: Lunes 2 de Noviembre de 2026 -> Próximo lunes debe ser el siguiente: 9 de Noviembre de 2026
        var monday = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
        var nextMondayFromMon = CreatePlanDraftCommandHandler.CalculateNextMondayInArgentina(monday);
        Assert.Equal(DayOfWeek.Monday, nextMondayFromMon.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 11, 9), nextMondayFromMon);
    }
}
