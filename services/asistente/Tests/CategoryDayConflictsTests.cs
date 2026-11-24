using System.Net;
using System.Reflection;
using System.Text;
using API.Controllers;
using API.Services;
using Application.DTOs.DayConflicts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Application.UseCases.DayConflicts.GetCategoryDayConflicts;
using Application.UseCases.DayConflicts.GetDayConflicts;
using Application.UseCases.PfDecisions.GetAthleteDecisions;
using Infrastructure.Services.Seguimiento;
using Infrastructure.Services.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y con clientes HTTP simulados para la detección de conflictos del día
/// por categoría en una sola llamada (KAN-574).
/// </summary>
public sealed class CategoryDayConflictsTests
{
    private static readonly Guid CategoryId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid OtherPfCategoryId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid AthleteConflictId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AthletePainNoConflictId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AthleteNoPainId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AthleteNoCheckInId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid BoxJumpId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BenchPressId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static SeguimientoClient CreateSeguimientoClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://seguimiento:8080/") }, NullLogger<SeguimientoClient>.Instance);

    private static TrainingPlanServiceClient CreateTrainingClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://entrenamiento:8080/") }, NullLogger<TrainingPlanServiceClient>.Instance);

    [Fact]
    public async Task Given_AthleteWithPainAndDayAlreadyAdjusted_When_RequestingCategoryDayConflicts_Then_ReportsAdjustedTodayWithoutCheckingRestrictions()
    {
        // KAN-660 (hallazgo 3): si el PF ya ajustó la sesión de hoy, no se vuelve a ofrecer el ajuste
        var seguimientoHandler = new FakeHttpHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith($"/api/v1/categories/{CategoryId}/traffic-light-today")
                ? Json(HttpStatusCode.OK, $$"""
                [
                    {
                        "athleteId": "{{AthleteConflictId}}",
                        "athleteName": "Diego",
                        "trafficLight": "Yellow",
                        "hasReportedToday": true,
                        "indicators": { "painZones": "cuadriceps:2" }
                    }
                ]
                """)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var restrictionChecks = 0;
        var trainingHandler = new FakeHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith($"/api/v1/athletes/{AthleteConflictId}/today-routine"))
            {
                return Json(HttpStatusCode.OK, $$"""
                {
                    "athleteId": "{{AthleteConflictId}}",
                    "date": "2026-11-21",
                    "adjustedByCoach": true,
                    "exercises": [
                        { "exerciseId": "{{BoxJumpId}}", "name": "Salto al cajón" }
                    ]
                }
                """);
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/exercises/restrictions/check"))
            {
                restrictionChecks++;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var handler = new GetCategoryDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        var response = await handler.HandleAsync(new GetCategoryDayConflictsQuery(CategoryId));

        var athlete = Assert.Single(response.Athletes);
        Assert.False(athlete.HasConflicts);
        Assert.True(athlete.AdjustedToday);
        Assert.Equal(0, restrictionChecks);
    }

    [Fact]
    public async Task Given_CategoryWithMixedAthletes_When_RequestingCategoryDayConflicts_Then_EvaluatesOnlyAthletesWithPainInOneSeguimientoCall()
    {
        var seguimientoHandler = new FakeHttpHandler(request =>
        {
            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.EndsWith($"/api/v1/categories/{CategoryId}/traffic-light-today"))
            {
                return Json(HttpStatusCode.OK, $$"""
                [
                    {
                        "athleteId": "{{AthleteConflictId}}",
                        "athleteName": "Diego",
                        "trafficLight": "Yellow",
                        "hasReportedToday": true,
                        "indicators": { "painZones": "cuadriceps:2" }
                    },
                    {
                        "athleteId": "{{AthletePainNoConflictId}}",
                        "athleteName": "Valentina",
                        "trafficLight": "Yellow",
                        "hasReportedToday": true,
                        "indicators": { "painZones": "hombro:2" }
                    },
                    {
                        "athleteId": "{{AthleteNoPainId}}",
                        "athleteName": "Martín",
                        "trafficLight": "Green",
                        "hasReportedToday": true,
                        "indicators": { "painZones": null }
                    },
                    {
                        "athleteId": "{{AthleteNoCheckInId}}",
                        "athleteName": "Nicolás",
                        "trafficLight": "Yellow",
                        "hasReportedToday": false,
                        "indicators": null
                    }
                ]
                """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var trainingHandler = new FakeHttpHandler(request =>
        {
            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.EndsWith($"/api/v1/athletes/{AthleteConflictId}/today-routine"))
            {
                return Json(HttpStatusCode.OK, $$"""
                {
                    "athleteId": "{{AthleteConflictId}}",
                    "date": "2026-11-21",
                    "exercises": [
                        { "exerciseId": "{{BoxJumpId}}", "name": "Salto al cajón" }
                    ]
                }
                """);
            }

            if (request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.EndsWith($"/api/v1/athletes/{AthletePainNoConflictId}/today-routine"))
            {
                return Json(HttpStatusCode.OK, $$"""
                {
                    "athleteId": "{{AthletePainNoConflictId}}",
                    "date": "2026-11-21",
                    "exercises": [
                        { "exerciseId": "{{BenchPressId}}", "name": "Press de banca" }
                    ]
                }
                """);
            }

            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/api/v1/exercises/restrictions/check"))
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var restricted = body.Contains("cuadriceps", StringComparison.OrdinalIgnoreCase);
                var exId = restricted ? BoxJumpId : BenchPressId;
                return Json(HttpStatusCode.OK, $$"""
                [
                    {
                        "exerciseId": "{{exId}}",
                        "restricted": {{(restricted ? "true" : "false")}},
                        "zone": {{(restricted ? "\"cuadriceps\"" : "null")}},
                        "painLevel": {{(restricted ? "2" : "null")}},
                        "reason": {{(restricted ? "\"Sobrecarga de cuádriceps\"" : "null")}},
                        "substitutes": []
                    }
                ]
                """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var handler = new GetCategoryDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        var response = await handler.HandleAsync(new GetCategoryDayConflictsQuery(CategoryId));

        Assert.Equal(CategoryId, response.CategoryId);
        Assert.Equal(4, response.Athletes.Count);

        // Conserva el orden original de la categoría y marca solo al atleta con conflicto real.
        Assert.Equal(AthleteConflictId, response.Athletes[0].AthleteId);
        Assert.True(response.Athletes[0].HasConflicts);

        Assert.Equal(AthletePainNoConflictId, response.Athletes[1].AthleteId);
        Assert.False(response.Athletes[1].HasConflicts);

        Assert.Equal(AthleteNoPainId, response.Athletes[2].AthleteId);
        Assert.False(response.Athletes[2].HasConflicts);

        Assert.Equal(AthleteNoCheckInId, response.Athletes[3].AthleteId);
        Assert.False(response.Athletes[3].HasConflicts);

        // Una sola llamada a Seguimiento para toda la categoría.
        Assert.Single(seguimientoHandler.Requests);

        // Solo los 2 atletas con dolor consultaron Entrenamiento (2 GET rutina + 2 POST restricciones = 4).
        Assert.Equal(4, trainingHandler.Requests.Count);
        Assert.DoesNotContain(trainingHandler.Requests, r => r.RequestUri!.AbsolutePath.Contains(AthleteNoPainId.ToString()));
        Assert.DoesNotContain(trainingHandler.Requests, r => r.RequestUri!.AbsolutePath.Contains(AthleteNoCheckInId.ToString()));
    }

    [Fact]
    public async Task Given_TwoAthletesWithSamePainAndSameRoutineExercises_When_RequestingCategoryDayConflicts_Then_DeduplicatesRestrictionCheckCall()
    {
        var seguimientoHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        [
            {
                "athleteId": "{{AthleteConflictId}}",
                "hasReportedToday": true,
                "indicators": { "painZones": "cuadriceps:2" }
            },
            {
                "athleteId": "{{AthletePainNoConflictId}}",
                "hasReportedToday": true,
                "indicators": { "painZones": "Cuadriceps:2 " }
            }
        ]
        """));

        var restrictionChecksCount = 0;
        var trainingHandler = new FakeHttpHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/today-routine"))
            {
                return Json(HttpStatusCode.OK, $$"""
                {
                    "athleteId": "{{AthleteConflictId}}",
                    "date": "2026-11-21",
                    "exercises": [
                        { "exerciseId": "{{BenchPressId}}", "name": "Press de banca" },
                        { "exerciseId": "{{BoxJumpId}}", "name": "Salto al cajón" }
                    ]
                }
                """);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/api/v1/exercises/restrictions/check"))
            {
                Interlocked.Increment(ref restrictionChecksCount);
                return Json(HttpStatusCode.OK, $$"""
                [
                    {
                        "exerciseId": "{{BoxJumpId}}",
                        "restricted": true,
                        "zone": "cuadriceps",
                        "painLevel": 2,
                        "reason": "Impacto reactivo",
                        "substitutes": []
                    }
                ]
                """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var handler = new GetCategoryDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        var response = await handler.HandleAsync(new GetCategoryDayConflictsQuery(CategoryId));

        Assert.Equal(2, response.Athletes.Count);
        Assert.All(response.Athletes, a => Assert.True(a.HasConflicts));
        Assert.Equal(1, restrictionChecksCount);
    }

    [Fact]
    public async Task Given_CategoryWithoutAnyPainReports_When_RequestingCategoryDayConflicts_Then_DoesNotCallTraining()
    {
        var seguimientoHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        [
            {
                "athleteId": "{{AthleteNoPainId}}",
                "hasReportedToday": true,
                "indicators": { "painZones": null }
            },
            {
                "athleteId": "{{AthleteNoCheckInId}}",
                "hasReportedToday": false,
                "indicators": null
            }
        ]
        """));

        var trainingHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var handler = new GetCategoryDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        var response = await handler.HandleAsync(new GetCategoryDayConflictsQuery(CategoryId));

        Assert.Equal(2, response.Athletes.Count);
        Assert.All(response.Athletes, a => Assert.False(a.HasConflicts));
        Assert.Empty(trainingHandler.Requests);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_OtherPfCategoryOrEmptyGuid_When_RequestingCategoryDayConflicts_Then_ThrowsNotFoundAndDoesNotCallTraining()
    {
        var seguimientoHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.NotFound, """{"title":"La categoría no existe."}"""));
        var trainingHandler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var handler = new GetCategoryDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetCategoryDayConflictsQuery(Guid.Empty)));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetCategoryDayConflictsQuery(OtherPfCategoryId)));

        Assert.Empty(trainingHandler.Requests);
    }

    [Fact]
    public async Task Given_AthleteWithPainWithoutRoutineToday_When_RequestingCategoryDayConflicts_Then_ReturnsFalseWithoutThrowing()
    {
        var seguimientoHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        [
            {
                "athleteId": "{{AthleteConflictId}}",
                "hasReportedToday": true,
                "indicators": { "painZones": "cuadriceps:3" }
            }
        ]
        """));

        var trainingHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        {
            "athleteId": "{{AthleteConflictId}}",
            "date": "2026-11-21",
            "exercises": []
        }
        """));

        var handler = new GetCategoryDayConflictsQueryHandler(
            CreateTrainingClient(trainingHandler),
            CreateSeguimientoClient(seguimientoHandler));

        var response = await handler.HandleAsync(new GetCategoryDayConflictsQuery(CategoryId));

        var item = Assert.Single(response.Athletes);
        Assert.Equal(AthleteConflictId, item.AthleteId);
        Assert.False(item.HasConflicts);
        Assert.Single(trainingHandler.Requests);
    }

    [Fact]
    public async Task Given_SeguimientoOrTrainingUnavailable_When_RequestingCategoryDayConflicts_Then_ThrowsServiceUnavailable()
    {
        var failingSeguimiento = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var okTraining = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var handler1 = new GetCategoryDayConflictsQueryHandler(
            CreateTrainingClient(okTraining),
            CreateSeguimientoClient(failingSeguimiento));

        await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(() =>
            handler1.HandleAsync(new GetCategoryDayConflictsQuery(CategoryId)));

        var okSeguimiento = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        [
            {
                "athleteId": "{{AthleteConflictId}}",
                "hasReportedToday": true,
                "indicators": { "painZones": "cuadriceps:3" }
            }
        ]
        """));
        var failingTraining = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var handler2 = new GetCategoryDayConflictsQueryHandler(
            CreateTrainingClient(failingTraining),
            CreateSeguimientoClient(okSeguimiento));

        await Assert.ThrowsAsync<TrainingServiceUnavailableException>(() =>
            handler2.HandleAsync(new GetCategoryDayConflictsQuery(CategoryId)));
    }

    [Fact]
    public async Task Given_RequestWithToken_When_CallingCategoryTrafficLight_Then_ForwardsAuthorizationHeader()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer token-del-pf";
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var inner = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, "[]"));
        var forward = new ForwardAuthorizationHandler(accessor.Object) { InnerHandler = inner };
        var seguimientoClient = CreateSeguimientoClient(forward);

        await seguimientoClient.GetCategoryTrafficLightTodayAsync(CategoryId);

        var sent = Assert.Single(inner.Requests);
        Assert.Equal("Bearer token-del-pf", sent.Headers.Authorization!.ToString());
        Assert.Equal($"http://seguimiento:8080/api/v1/categories/{CategoryId}/traffic-light-today", sent.RequestUri!.ToString());
    }

    [Fact]
    public void Given_AssistantController_When_InspectingGetCategoryDayConflictsRouteAndRole_Then_RequiresPreparadorFisicoAndExposesExpectedRoute()
    {
        var authAttribute = typeof(AssistantController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttribute);
        Assert.Equal("PreparadorFisico", authAttribute.Roles);

        var method = typeof(AssistantController).GetMethod(nameof(AssistantController.GetCategoryDayConflicts));
        Assert.NotNull(method);

        var httpGet = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("categories/{categoryId:guid}/day-conflicts", httpGet.Template);
    }

    [Fact]
    public async Task Given_AssistantController_When_CallingGetCategoryDayConflicts_Then_Returns200OkWithCategoryConflicts()
    {
        var trainingMock = new Mock<ITrainingPlanServiceClient>();
        var seguimientoMock = new Mock<ISeguimientoClient>();

        seguimientoMock
            .Setup(c => c.GetCategoryTrafficLightTodayAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new CategoryTrafficLightAthleteRemoteDto(
                    AthleteConflictId,
                    HasReportedToday: true,
                    new TrafficLightIndicatorsRemoteDto("cuadriceps:2"))
            ]);

        trainingMock
            .Setup(c => c.GetTodayRoutineAsync(AthleteConflictId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayRoutineRemoteDto(
                AthleteConflictId,
                new DateOnly(2026, 11, 21),
                [new TodayRoutineExerciseRemoteDto(BoxJumpId, "Salto al cajón")]));

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
                    Substitutes: [])
            ]);

        var controller = new AssistantController(
            new SuggestRoutineUseCase(
                Mock.Of<IAiService>(),
                Mock.Of<IAthleteAdaptationContextService>(),
                Mock.Of<IStructureAthleteClient>()),
            new GetAthleteDecisionsQueryHandler(Mock.Of<IPfDecisionRepository>(), Mock.Of<IStructureAthleteClient>()),
            new GetDayConflictsQueryHandler(trainingMock.Object, seguimientoMock.Object),
            getCategoryDayConflictsHandler: new GetCategoryDayConflictsQueryHandler(trainingMock.Object, seguimientoMock.Object));

        var actionResult = await controller.GetCategoryDayConflicts(CategoryId, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var dto = Assert.IsType<CategoryDayConflictsResponseDto>(okResult.Value);
        Assert.Equal(CategoryId, dto.CategoryId);
        var item = Assert.Single(dto.Athletes);
        Assert.Equal(AthleteConflictId, item.AthleteId);
        Assert.True(item.HasConflicts);
    }
}
