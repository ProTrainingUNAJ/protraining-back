using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using API.Controllers;
using API.Middleware;
using API.Services;
using Application.DTOs.DayConflicts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Application.UseCases.DayConflicts.GetDayConflicts;
using Application.UseCases.PfDecisions.GetAthleteDecisions;
using Infrastructure.Services.Seguimiento;
using Infrastructure.Services.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y con clientes HTTP simulados para la detección de conflictos del día
/// entre el dolor del check-in y la rutina de hoy (#156 · KAN-451).
/// </summary>
public sealed class DayConflictsTests
{
    private static readonly Guid AthleteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherPfAthleteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BoxJumpId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BenchPressId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegPressId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid BandSquatId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SeguimientoClient CreateSeguimientoClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://seguimiento:8080/") }, NullLogger<SeguimientoClient>.Instance);

    private static TrainingPlanServiceClient CreateTrainingClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://entrenamiento:8080/") }, NullLogger<TrainingPlanServiceClient>.Instance);

    // =========================================================================
    // CRITERIO 1: Atleta con cuadriceps:2 en el check-in de hoy y saltos en su rutina
    // =========================================================================
    [Fact]
    public async Task Given_AthleteWithQuadricepsPainAndJumpsInRoutine_When_PfRequestsConflicts_Then_ReturnsJumpConflictWithSubstitutes()
    {
        var seguimientoHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        {
            "id": "{{Guid.NewGuid()}}",
            "athleteId": "{{AthleteId}}",
            "date": "2026-11-05T00:00:00",
            "trafficLight": "Yellow",
            "painZones": "cuadriceps:2"
        }
        """));

        var trainingHandler = new FakeHttpHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith($"/api/v1/athletes/{AthleteId}/today-routine"))
            {
                return Json(HttpStatusCode.OK, $$"""
                {
                    "athleteId": "{{AthleteId}}",
                    "athleteName": "Lucas Fernández",
                    "date": "2026-11-05",
                    "exercises": [
                        { "exerciseId": "{{BoxJumpId}}", "name": "Salto al cajón (respuesta múltiple)" },
                        { "exerciseId": "{{BenchPressId}}", "name": "Press de banca con barra - Agarre medio" }
                    ]
                }
                """);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/api/v1/exercises/restrictions/check"))
            {
                return Json(HttpStatusCode.OK, $$"""
                [
                    {
                        "exerciseId": "{{BoxJumpId}}",
                        "restricted": true,
                        "zone": "cuadriceps",
                        "painLevel": 2,
                        "reason": "Impacto reactivo y desaceleración brusca que sobrecargan el tendón rotuliano y los cuádriceps.",
                        "substitutes": [
                            { "exerciseId": "{{LegPressId}}", "name": "Prensa de piernas" },
                            { "exerciseId": "{{BandSquatId}}", "name": "Sentadillas con bandas elásticas" }
                        ]
                    },
                    {
                        "exerciseId": "{{BenchPressId}}",
                        "restricted": false,
                        "zone": null,
                        "painLevel": null,
                        "reason": null,
                        "substitutes": []
                    }
                ]
                """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var handler = new GetDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        var response = await handler.HandleAsync(new GetDayConflictsQuery(AthleteId));

        Assert.True(response.HasConflicts);
        Assert.Equal("cuadriceps:2", response.PainZones);
        Assert.Null(response.Message);

        var conflict = Assert.Single(response.Conflicts);
        Assert.Equal(BoxJumpId, conflict.ExerciseId);
        Assert.Equal("Salto al cajón (respuesta múltiple)", conflict.Name);
        Assert.Equal("cuadriceps", conflict.Zone);
        Assert.Equal(2, conflict.PainLevel);
        Assert.Contains("tendón rotuliano", conflict.Reason);
        Assert.Equal(2, conflict.Substitutes.Count);
        Assert.Equal(LegPressId, conflict.Substitutes[0].ExerciseId);
        Assert.Equal("Prensa de piernas", conflict.Substitutes[0].Name);
        Assert.Equal(BandSquatId, conflict.Substitutes[1].ExerciseId);
        Assert.Equal("Sentadillas con bandas elásticas", conflict.Substitutes[1].Name);
    }

    // =========================================================================
    // CRITERIO 2a: Atleta sin dolor en el check-in de hoy → hasConflicts = false
    // =========================================================================
    [Fact]
    public async Task Given_AthleteWithoutPainInCheckIn_When_RequestingConflicts_Then_HasConflictsIsFalse()
    {
        var seguimientoHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        {
            "id": "{{Guid.NewGuid()}}",
            "athleteId": "{{AthleteId}}",
            "date": "2026-11-05T00:00:00",
            "trafficLight": "Green",
            "painZones": null
        }
        """));

        var restrictionsCalled = false;
        var trainingHandler = new FakeHttpHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, $$"""
                {
                    "athleteId": "{{AthleteId}}",
                    "date": "2026-11-05",
                    "exercises": [
                        { "exerciseId": "{{BoxJumpId}}", "name": "Salto al cajón (respuesta múltiple)" }
                    ]
                }
                """);
            }

            restrictionsCalled = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new GetDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        var response = await handler.HandleAsync(new GetDayConflictsQuery(AthleteId));

        Assert.False(response.HasConflicts);
        Assert.Null(response.PainZones);
        Assert.Empty(response.Conflicts);
        Assert.Null(response.Message);
        Assert.False(restrictionsCalled);
    }

    // =========================================================================
    // CRITERIO 2b: Atleta sin check-in hoy → hasConflicts = false y aviso "Todavía no hizo el check-in"
    // =========================================================================
    [Fact]
    public async Task Given_AthleteWithoutCheckInToday_When_RequestingConflicts_Then_HasConflictsIsFalseWithMissingCheckInNotice()
    {
        var seguimientoHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.NotFound, """{"title":"El atleta todavía no hizo el check-in de hoy."}"""));
        var trainingHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        {
            "athleteId": "{{AthleteId}}",
            "date": "2026-11-05",
            "exercises": [
                { "exerciseId": "{{BoxJumpId}}", "name": "Salto al cajón (respuesta múltiple)" }
            ]
        }
        """));

        var handler = new GetDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        var response = await handler.HandleAsync(new GetDayConflictsQuery(AthleteId));

        Assert.False(response.HasConflicts);
        Assert.Null(response.PainZones);
        Assert.Empty(response.Conflicts);
        Assert.Equal("Todavía no hizo el check-in", response.Message);
    }

    // =========================================================================
    // CRITERIO 3: PF que pide conflictos de un atleta de otro PF → 404
    // =========================================================================
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_PfRequestingConflictsOfAnotherPfAthlete_When_Calling_Then_ThrowsNotFoundAndReturns404()
    {
        var trainingHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.NotFound, """{"title":"El atleta no existe o no es tuyo"}"""));
        var seguimientoHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var handler = new GetDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetDayConflictsQuery(OtherPfAthleteId)));

        Assert.Null(seguimientoHandler.LastRequest);
    }

    // =========================================================================
    // CRITERIO 4 y PUNTO 5: Clientes HTTP simulados (como StructureClientTests) y 503 en español
    // =========================================================================
    [Fact]
    public async Task Given_RequestWithToken_When_CallingSeguimientoAndTraining_Then_ForwardsSameAuthorizationHeader()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer token-del-pf";
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var seguimientoInner = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""{"id":"{{Guid.NewGuid()}}","athleteId":"{{AthleteId}}","date":"2026-11-05T00:00:00","painZones":"cuadriceps:2"}"""));
        var seguimientoForward = new ForwardAuthorizationHandler(accessor.Object) { InnerHandler = seguimientoInner };
        var seguimientoClient = CreateSeguimientoClient(seguimientoForward);

        var trainingInner = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""{"athleteId":"{{AthleteId}}","date":"2026-11-05","exercises":[]}"""));
        var trainingForward = new ForwardAuthorizationHandler(accessor.Object) { InnerHandler = trainingInner };
        var trainingClient = CreateTrainingClient(trainingForward);

        await seguimientoClient.GetTodayCheckInAsync(AthleteId);
        await trainingClient.GetTodayRoutineAsync(AthleteId);

        Assert.Equal("Bearer token-del-pf", seguimientoInner.LastRequest!.Headers.Authorization!.ToString());
        Assert.Equal($"http://seguimiento:8080/api/v1/athletes/{AthleteId}/check-in/today", seguimientoInner.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer token-del-pf", trainingInner.LastRequest!.Headers.Authorization!.ToString());
        Assert.Equal($"http://entrenamiento:8080/api/v1/athletes/{AthleteId}/today-routine", trainingInner.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task Given_SeguimientoFailingWith500_When_GetTodayCheckIn_Then_ThrowsSeguimientoUnavailableAndMiddlewareReturns503InSpanish()
    {
        var client = CreateSeguimientoClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var ex = await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(() =>
            client.GetTodayCheckInAsync(AthleteId));
        Assert.Contains("Seguimiento", ex.Message);

        var (status, body) = await ExecuteThroughExceptionMiddlewareAsync(() => throw ex);
        Assert.Equal(503, status);
        Assert.Contains("Seguimiento", body);
    }

    [Fact]
    public async Task Given_SeguimientoUnreachableOrCircuitOpen_When_GetTodayCheckIn_Then_ThrowsSeguimientoUnavailable()
    {
        var unreachableClient = CreateSeguimientoClient(new FakeHttpHandler(_ => throw new HttpRequestException("Connection refused")));
        await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(() => unreachableClient.GetTodayCheckInAsync(AthleteId));

        var circuitClient = CreateSeguimientoClient(new FakeHttpHandler(_ => throw new BrokenCircuitException()));
        await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(() => circuitClient.GetTodayCheckInAsync(AthleteId));

        var timeoutClient = CreateSeguimientoClient(new FakeHttpHandler(_ => throw new TimeoutRejectedException()));
        await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(() => timeoutClient.GetTodayCheckInAsync(AthleteId));
    }

    [Fact]
    public async Task Given_TrainingFailingOrUnreachable_When_GetTodayRoutineOrCheckRestrictions_Then_ThrowsTrainingUnavailableAndMiddlewareReturns503InSpanish()
    {
        var failingClient = CreateTrainingClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var exRoutine = await Assert.ThrowsAsync<TrainingServiceUnavailableException>(() =>
            failingClient.GetTodayRoutineAsync(AthleteId));
        Assert.Contains("Entrenamiento", exRoutine.Message);

        var exRestrictions = await Assert.ThrowsAsync<TrainingServiceUnavailableException>(() =>
            failingClient.CheckExerciseRestrictionsAsync("cuadriceps:2", [BoxJumpId]));
        Assert.Contains("Entrenamiento", exRestrictions.Message);

        var (status, body) = await ExecuteThroughExceptionMiddlewareAsync(() => throw exRoutine);
        Assert.Equal(503, status);
        Assert.Contains("Entrenamiento", body);
    }

    [Fact]
    public async Task Given_TrainingOpenCircuitOrTimeout_When_GetTodayRoutine_Then_ThrowsTrainingUnavailable()
    {
        var circuitClient = CreateTrainingClient(new FakeHttpHandler(_ => throw new BrokenCircuitException()));
        await Assert.ThrowsAsync<TrainingServiceUnavailableException>(() => circuitClient.GetTodayRoutineAsync(AthleteId));

        var timeoutClient = CreateTrainingClient(new FakeHttpHandler(_ => throw new TimeoutRejectedException()));
        await Assert.ThrowsAsync<TrainingServiceUnavailableException>(() => timeoutClient.GetTodayRoutineAsync(AthleteId));
    }

    [Fact]
    public void Given_AssistantController_When_InspectingGetDayConflictsRouteAndRole_Then_RequiresPreparadorFisicoAndExposesExpectedRoute()
    {
        var authAttribute = typeof(AssistantController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttribute);
        Assert.Equal("PreparadorFisico", authAttribute.Roles);

        var method = typeof(AssistantController).GetMethod(nameof(AssistantController.GetDayConflicts));
        Assert.NotNull(method);

        var httpGet = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("athletes/{athleteId:guid}/day-conflicts", httpGet.Template);
    }

    [Fact]
    public async Task Given_AssistantController_When_CallingGetDayConflicts_Then_Returns200OkWithConflicts()
    {
        var trainingMock = new Mock<ITrainingPlanServiceClient>();
        var seguimientoMock = new Mock<ISeguimientoClient>();

        trainingMock
            .Setup(c => c.GetTodayRoutineAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayRoutineRemoteDto(
                AthleteId,
                new DateOnly(2026, 11, 5),
                [new TodayRoutineExerciseRemoteDto(BoxJumpId, "Salto al cajón (respuesta múltiple)")]));

        seguimientoMock
            .Setup(c => c.GetTodayCheckInAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayCheckInDto(Guid.NewGuid(), AthleteId, new DateTime(2026, 11, 5), "cuadriceps:2"));

        trainingMock
            .Setup(c => c.CheckExerciseRestrictionsAsync("cuadriceps:2", It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ExerciseRestrictionCheckResultRemoteDto(
                    BoxJumpId,
                    Restricted: true,
                    Zone: "cuadriceps",
                    PainLevel: 2,
                    Reason: "Impacto reactivo",
                    Substitutes: [new DayConflictSubstituteDto(LegPressId, "Prensa de piernas")])
            ]);

        var controller = new AssistantController(
            new SuggestRoutineUseCase(
                Mock.Of<IAiService>(),
                Mock.Of<IAthleteAdaptationContextService>(),
                Mock.Of<IStructureAthleteClient>()),
            new GetAthleteDecisionsQueryHandler(Mock.Of<IPfDecisionRepository>(), Mock.Of<IStructureAthleteClient>()),
            new GetDayConflictsQueryHandler(trainingMock.Object, seguimientoMock.Object));

        var actionResult = await controller.GetDayConflicts(AthleteId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var dto = Assert.IsType<DayConflictsResponseDto>(okResult.Value);
        Assert.True(dto.HasConflicts);
        Assert.Single(dto.Conflicts);
    }

    private static async Task<(int StatusCode, string Body)> ExecuteThroughExceptionMiddlewareAsync(Func<Task> nextAction)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        using var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        var middleware = new ExceptionMiddleware(_ => nextAction(), NullLogger<ExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        responseStream.Position = 0;
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }
}
