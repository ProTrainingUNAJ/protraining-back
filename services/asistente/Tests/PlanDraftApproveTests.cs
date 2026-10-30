using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using API.Controllers;
using API.Middleware;
using API.Services;
using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.ApprovePlanDraft;
using Domain.Entities;
using Infrastructure.Services.Training;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y de integración HTTP (con handler simulado) para la aprobación de borradores
/// y su envío al microservicio de Entrenamiento (#116 · KAN-411).
/// </summary>
public sealed class PlanDraftApproveTests
{
    private static readonly Guid CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AthleteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SquatId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BenchPressId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly StartMonday = new(2026, 11, 2);

    [Fact]
    public void Given_PlanDraftWithMatchDayNotes_When_MappedToSaveTrainingPlanRequest_Then_CopiesAllFieldsAndCoachNotes()
    {
        // Arrange
        var draft = CreateSampleDraft(
            forAthlete: false,
            coachNotes: "Cuidar técnica | Ajustado por el partido del sábado 7/11: Sentadilla trasera con barra 85 % → 60 %");

        // Act
        var request = PlanDraftMapper.ToSaveTrainingPlanRequest(draft);

        // Assert
        Assert.Equal("Mesociclo de Fuerza Base", request.Name);
        Assert.Equal("Subir fuerza máxima en sentadilla", request.Objective);
        Assert.NotNull(request.Weeks);
        var week = Assert.Single(request.Weeks!);
        Assert.Equal(1, week.WeekNumber);
        Assert.NotNull(week.Sessions);
        var session = Assert.Single(week.Sessions!);
        Assert.Equal("Día 1 - Tren Inferior", session.Name);
        Assert.Equal(DayOfWeek.Friday, session.DayOfWeek);
        Assert.Equal(1, session.Order);
        Assert.Contains("Ajustado por el partido del sábado 7/11", session.CoachNotes);
        Assert.NotNull(session.Exercises);
        Assert.Equal(2, session.Exercises!.Count);
        Assert.Equal(SquatId, session.Exercises[0].ExerciseId);
        Assert.Equal(4, session.Exercises[0].Sets);
        Assert.Equal(6, session.Exercises[0].Reps);
        Assert.Equal(60m, session.Exercises[0].TargetPercentage);
    }

    [Fact]
    public void Given_PlanDraftWithEmptyContentObjective_When_MappedToSaveTrainingPlanRequest_Then_UsesPfGoalAsObjective()
    {
        // Arrange
        var content = new PlanDraftContentDto(
            Name: "Plan Pretemporada",
            Objective: "   ",
            JustificationSummary: "Resumen",
            Weeks: []);

        // Act
        var request = PlanDraftMapper.ToSaveTrainingPlanRequest(content, fallbackObjective: "Fuerza explosiva de piernas");

        // Assert
        Assert.Equal("Fuerza explosiva de piernas", request.Objective);
    }

    [Fact]
    public async Task Given_ValidDraft_When_PfApproves_Then_CreatesAndAssignsPlanInEntrenamientoForwardsTokenAndMarksApproved()
    {
        // Arrange
        var draft = CreateSampleDraft(forAthlete: false);
        var expectedPlanId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var expectedAssignmentId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        const string pfToken = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.pf-token";

        var fakeHandler = new FakeEntrenamientoHttpHandler((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/v1/training-plans", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { id = expectedPlanId, name = "Mesociclo de Fuerza Base" }),
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (req.RequestUri.AbsolutePath.EndsWith("/api/v1/training-plans/assignments", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { assignmentId = expectedAssignmentId, message = "Plan asignado exitosamente." }),
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = CreateHttpClientWithForwardedAuth(fakeHandler, pfToken);
        var (handler, _, uowMock) = CreateApproveHandler(draft, client);

        // Act
        var result = await handler.HandleAsync(new ApprovePlanDraftCommand(draft.Id));

        // Assert: Respuesta y estado del borrador
        Assert.Equal(draft.Id, result.Id);
        Assert.Equal(expectedPlanId, result.PlanId);
        Assert.Equal(expectedPlanId, result.ApprovedPlanId);
        Assert.Equal("Approved", result.Status);
        Assert.True(result.Assigned);
        Assert.Equal(expectedAssignmentId, result.AssignmentId);
        Assert.Null(result.Warning);
        Assert.Empty(result.Warnings ?? []);

        Assert.Equal(PlanDraftStatus.Approved, draft.Status);
        Assert.Equal(expectedPlanId, draft.ApprovedPlanId);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Assert: Llamadas HTTP enviadas a Entrenamiento con el token del PF reenviado
        Assert.Equal(2, fakeHandler.RecordedRequests.Count);

        var createCall = fakeHandler.RecordedRequests[0];
        Assert.Equal(HttpMethod.Post, createCall.Method);
        Assert.EndsWith("/api/v1/training-plans", createCall.Path);
        Assert.Equal(pfToken, createCall.AuthorizationHeader);
        using (var doc = JsonDocument.Parse(createCall.Body))
        {
            Assert.Equal("Mesociclo de Fuerza Base", doc.RootElement.GetProperty("name").GetString());
            Assert.Equal("Subir fuerza máxima en sentadilla", doc.RootElement.GetProperty("objective").GetString());
            var firstSession = doc.RootElement.GetProperty("weeks")[0].GetProperty("sessions")[0];
            Assert.Equal((int)DayOfWeek.Friday, firstSession.GetProperty("dayOfWeek").GetInt32());
            Assert.Contains("Cuidar técnica", firstSession.GetProperty("coachNotes").GetString());
        }

        var assignCall = fakeHandler.RecordedRequests[1];
        Assert.Equal(HttpMethod.Post, assignCall.Method);
        Assert.EndsWith("/api/v1/training-plans/assignments", assignCall.Path);
        Assert.Equal(pfToken, assignCall.AuthorizationHeader);
        using (var doc = JsonDocument.Parse(assignCall.Body))
        {
            Assert.Equal(expectedPlanId, doc.RootElement.GetProperty("trainingPlanId").GetGuid());
            Assert.Equal("2026-11-02", doc.RootElement.GetProperty("startDate").GetString());
            Assert.Equal(CategoryId, doc.RootElement.GetProperty("categoryId").GetGuid());
        }
    }

    [Fact]
    public async Task Given_EntrenamientoIsDownOnCreate_When_PfApproves_Then_Throws503AndDraftRemainsInDraft()
    {
        // Arrange
        var draft = CreateSampleDraft();
        var fakeHandler = new FakeEntrenamientoHttpHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var client = CreateHttpClientWithForwardedAuth(fakeHandler, "Bearer token");
        var (handler, _, uowMock) = CreateApproveHandler(draft, client);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<TrainingServiceUnavailableException>(() =>
            handler.HandleAsync(new ApprovePlanDraftCommand(draft.Id)));

        Assert.Equal("No se pudo crear la planificación, probá de nuevo", ex.Message);
        Assert.Equal(PlanDraftStatus.Draft, draft.Status);
        Assert.Null(draft.ApprovedPlanId);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(fakeHandler.RecordedRequests);
    }

    [Fact]
    public async Task Given_TrainingServiceUnavailableException_When_HandledByExceptionMiddleware_Then_Returns503ProblemDetailsInSpanish()
    {
        // Arrange
        var middleware = new ExceptionMiddleware(
            _ => throw new TrainingServiceUnavailableException(),
            NullLogger<ExceptionMiddleware>.Instance);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        httpContext.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        Assert.Contains("No se pudo crear la planificación, probá de nuevo", body);
    }

    [Fact]
    public async Task Given_PlanCreatedButAssignmentFails_When_PfApproves_Then_SavesApprovedPlanIdAndReturnsWarning()
    {
        // Arrange
        var draft = CreateSampleDraft(forAthlete: true);
        var createdPlanId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        var fakeHandler = new FakeEntrenamientoHttpHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/v1/training-plans", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { id = createdPlanId }),
                        Encoding.UTF8,
                        "application/json")
                };
            }

            // Simular fallo en POST /api/v1/training-plans/assignments
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var client = CreateHttpClientWithForwardedAuth(fakeHandler, "Bearer token");
        var (handler, _, uowMock) = CreateApproveHandler(draft, client);

        // Act
        var result = await handler.HandleAsync(new ApprovePlanDraftCommand(draft.Id));

        // Assert: Se guarda igual approved_plan_id y se devuelve el aviso
        Assert.Equal(PlanDraftStatus.Approved, draft.Status);
        Assert.Equal(createdPlanId, draft.ApprovedPlanId);
        Assert.Equal(createdPlanId, result.PlanId);
        Assert.Equal(createdPlanId, result.ApprovedPlanId);
        Assert.False(result.Assigned);
        Assert.Null(result.AssignmentId);
        Assert.Equal("Se creó la planificación pero no se pudo asignar", result.Warning);
        Assert.Contains("Se creó la planificación pero no se pudo asignar", result.Warnings ?? []);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_AlreadyApprovedDraft_When_PfApprovesAgain_Then_DoesNotCallEntrenamientoAndReturnsExistingPlanId()
    {
        // Arrange
        var draft = CreateSampleDraft();
        var existingPlanId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        draft.Approve(existingPlanId);

        var fakeHandler = new FakeEntrenamientoHttpHandler((_, _) =>
            throw new InvalidOperationException("No debería invocarse a Entrenamiento si ya estaba aprobado."));

        var client = CreateHttpClientWithForwardedAuth(fakeHandler, "Bearer token");
        var (handler, _, uowMock) = CreateApproveHandler(draft, client);

        // Act
        var result = await handler.HandleAsync(new ApprovePlanDraftCommand(draft.Id));

        // Assert
        Assert.Equal(existingPlanId, result.PlanId);
        Assert.Equal(existingPlanId, result.ApprovedPlanId);
        Assert.Equal("Approved", result.Status);
        Assert.True(result.Assigned);
        Assert.Empty(fakeHandler.RecordedRequests);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_DiscardedDraft_When_PfAttemptsToApprove_Then_ThrowsConflictException409()
    {
        // Arrange
        var draft = CreateSampleDraft();
        draft.Discard();

        var fakeHandler = new FakeEntrenamientoHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateHttpClientWithForwardedAuth(fakeHandler, "Bearer token");
        var (handler, _, uowMock) = CreateApproveHandler(draft, client);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            handler.HandleAsync(new ApprovePlanDraftCommand(draft.Id)));

        Assert.Equal("El borrador ya fue descartado.", ex.Message);
        Assert.Empty(fakeHandler.RecordedRequests);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_DraftOfAnotherPfOrNonExistent_When_PfAttemptsToApprove_Then_ThrowsNotFoundException404()
    {
        // Arrange
        var fakeHandler = new FakeEntrenamientoHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateHttpClientWithForwardedAuth(fakeHandler, "Bearer token");
        var (handler, _, _) = CreateApproveHandler(existingDraft: null, client);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new ApprovePlanDraftCommand(Guid.NewGuid())));
    }

    [Fact]
    public void Given_PlanDraftsControllerApproveEndpoint_When_InspectingAttributes_Then_HasExpectedRouteMethodAndStatusCodes()
    {
        var method = typeof(PlanDraftsController).GetMethod(nameof(PlanDraftsController.Approve));
        Assert.NotNull(method);

        var postAttr = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(postAttr);
        Assert.Equal("{id:guid}/approve", postAttr!.Template);
    }

    private static PlanDraft CreateSampleDraft(
        bool forAthlete = false,
        string? coachNotes = "Cuidar técnica")
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
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 1,
                            CoachNotes: coachNotes,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(SquatId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 60m),
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

    private static ITrainingPlanServiceClient CreateHttpClientWithForwardedAuth(
        FakeEntrenamientoHttpHandler innerHandler,
        string bearerHeaderValue)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = bearerHeaderValue;

        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        var forwardHandler = new ForwardAuthorizationHandler(accessorMock.Object)
        {
            InnerHandler = innerHandler
        };

        var httpClient = new HttpClient(forwardHandler)
        {
            BaseAddress = new Uri("http://entrenamiento:8080/")
        };

        return new TrainingPlanServiceClient(httpClient, NullLogger<TrainingPlanServiceClient>.Instance);
    }

    private static (
        ApprovePlanDraftCommandHandler Handler,
        Mock<IPlanDraftRepository> DraftRepoMock,
        Mock<IUnitOfWork> UowMock)
        CreateApproveHandler(
            PlanDraft? existingDraft,
            ITrainingPlanServiceClient trainingClient)
    {
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        if (existingDraft is not null)
        {
            draftRepoMock
                .Setup(r => r.GetByIdAsync(existingDraft.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingDraft);
        }

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new ApprovePlanDraftCommandHandler(
            draftRepoMock.Object,
            trainingClient,
            uowMock.Object);

        return (handler, draftRepoMock, uowMock);
    }

    private sealed record RecordedHttpRequest(
        HttpMethod Method,
        string Path,
        string? AuthorizationHeader,
        string Body
    );

    private sealed class FakeEntrenamientoHttpHandler(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedHttpRequest> RecordedRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;

            RecordedRequests.Add(new RecordedHttpRequest(
                Method: request.Method,
                Path: request.RequestUri?.AbsolutePath ?? string.Empty,
                AuthorizationHeader: request.Headers.Authorization?.ToString(),
                Body: body));

            return responder(request, body);
        }
    }
}
