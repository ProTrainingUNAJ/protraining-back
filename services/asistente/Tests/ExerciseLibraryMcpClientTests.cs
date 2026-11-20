using System.ComponentModel;
using System.Net;
using System.Text.Json;
using API.Services;
using Application.DTOs.DayConflicts;
using Application.DTOs.Mcp;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.DayAdjustments.CreateDayAdjustment;
using Application.UseCases.DayConflicts.GetDayConflicts;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Rules;
using Infrastructure.Services.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y de integración del cliente MCP en el Asistente (#189 · KAN-484).
/// Levanta un servidor MCP real en memoria con el transporte oficial Streamable HTTP (/mcp)
/// y verifica la invocación de BuscarEjercicios, ObtenerSustitutos, ObtenerRestricciones,
/// listado dinámico de tools y la integración en el flujo de sustitutos del Asistente.
/// </summary>
public sealed class ExerciseLibraryMcpClientTests
{
    private static readonly Guid SquatId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LegPressId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid HipThrustId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AthleteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SessionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // =========================================================================
    // CRITERIO DE ACEPTACIÓN KAN-484:
    // DADO el servidor MCP levantado CUANDO el Asistente busca un sustituto ENTONCES lo obtiene por MCP.
    // =========================================================================
    [Fact]
    public async Task Given_McpServerRunning_When_AsistenteGetsSubstitutesForRodilla_Then_ObtainsSubstitutesViaMcp()
    {
        await using var mcpHost = await StartInMemoryMcpServerAsync();
        var client = new ExerciseLibraryMcpClient(
            mcpHost.CreateHttpClient(),
            NullLogger<ExerciseLibraryMcpClient>.Instance);

        var result = await client.GetSubstitutesAsync("rodilla");

        Assert.NotNull(result);
        Assert.Equal("rodilla", result.Zone);
        Assert.Equal(2, result.Substitutes.Count);
        Assert.Equal(LegPressId, result.Substitutes[0].ExerciseId);
        Assert.Equal("Prensa de piernas", result.Substitutes[0].Name);
        Assert.Equal(HipThrustId, result.Substitutes[1].ExerciseId);
        Assert.Equal("Empuje de cadera con barra", result.Substitutes[1].Name);
    }

    [Fact]
    public async Task Given_McpServerRunning_When_AsistenteCreatesDayAdjustmentWithoutPreloadedSubstitutes_Then_ResolvesSubstituteViaMcp()
    {
        await using var mcpHost = await StartInMemoryMcpServerAsync();
        var mcpClient = new ExerciseLibraryMcpClient(
            mcpHost.CreateHttpClient(),
            NullLogger<ExerciseLibraryMcpClient>.Instance);

        var trainingClientMock = new Mock<ITrainingPlanServiceClient>();
        trainingClientMock
            .Setup(c => c.GetTodayRoutineAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayRoutineRemoteDto(
                AthleteId,
                new DateOnly(2026, 11, 18),
                [new TodayRoutineExerciseRemoteDto(SquatId, "Sentadilla con barra")],
                SessionId));

        // Entrenamiento marca que el ejercicio está restringido para "rodilla", sin lista pre-cargada de sustitutos
        trainingClientMock
            .Setup(c => c.CheckExerciseRestrictionsAsync("rodilla:2", It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ExerciseRestrictionCheckResultRemoteDto(
                    ExerciseId: SquatId,
                    Restricted: true,
                    Zone: "rodilla",
                    PainLevel: 2,
                    Reason: "Carga axial sobre rodilla",
                    Substitutes: [])
            ]);

        var seguimientoClientMock = new Mock<ISeguimientoClient>();
        seguimientoClientMock
            .Setup(c => c.GetTodayCheckInAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayCheckInDto(
                Guid.NewGuid(),
                AthleteId,
                new DateTime(2026, 11, 18),
                "rodilla:2"));

        var getDayConflictsHandler = new GetDayConflictsQueryHandler(
            trainingClientMock.Object,
            seguimientoClientMock.Object,
            mcpClient);

        var storedAdjustments = new List<DayAdjustment>();
        var dayAdjustmentRepoMock = new Mock<IDayAdjustmentRepository>();
        dayAdjustmentRepoMock
            .Setup(r => r.GetPendingByAthleteAndDateAsync(AthleteId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayAdjustment?)null);
        dayAdjustmentRepoMock
            .Setup(r => r.AddAsync(It.IsAny<DayAdjustment>(), It.IsAny<CancellationToken>()))
            .Callback<DayAdjustment, CancellationToken>((adj, _) => storedAdjustments.Add(adj))
            .Returns(Task.CompletedTask);

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(a => a.GenerateDayAdjustmentJustificationAsync(It.IsAny<IReadOnlyList<Application.DTOs.DayAdjustments.DayAdjustmentItemDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("Se reemplaza Sentadilla con barra por Prensa de piernas por molestia en rodilla.", "Reglas"));

        var uowMock = new Mock<IUnitOfWork>();

        var createAdjustmentHandler = new CreateDayAdjustmentCommandHandler(
            dayAdjustmentRepoMock.Object,
            getDayConflictsHandler,
            aiServiceMock.Object,
            uowMock.Object,
            mcpClient);

        var response = await createAdjustmentHandler.HandleAsync(new CreateDayAdjustmentCommand(AthleteId));

        var item = Assert.Single(response.Items);
        Assert.Equal(SquatId, item.OriginalExerciseId);
        Assert.Equal(LegPressId, item.SuggestedSubstituteExerciseId);
        Assert.Equal("Prensa de piernas", item.SuggestedSubstituteExerciseName);
        Assert.Equal(2, item.AvailableSubstitutes.Count);
    }

    [Fact]
    public async Task Given_McpServerDown_When_GetDayConflictsRuns_Then_ReturnsConflictsWithoutSubstitutesInsteadOf503()
    {
        var trainingClientMock = new Mock<ITrainingPlanServiceClient>();
        trainingClientMock
            .Setup(c => c.GetTodayRoutineAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayRoutineRemoteDto(
                AthleteId,
                new DateOnly(2026, 11, 18),
                [new TodayRoutineExerciseRemoteDto(SquatId, "Sentadilla con barra")],
                SessionId));

        trainingClientMock
            .Setup(c => c.CheckExerciseRestrictionsAsync("rodilla:3", It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ExerciseRestrictionCheckResultRemoteDto(
                    ExerciseId: SquatId,
                    Restricted: true,
                    Zone: "rodilla",
                    PainLevel: 3,
                    Reason: "Carga axial sobre rodilla",
                    Substitutes: [])
            ]);

        var seguimientoClientMock = new Mock<ISeguimientoClient>();
        seguimientoClientMock
            .Setup(c => c.GetTodayCheckInAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayCheckInDto(
                Guid.NewGuid(),
                AthleteId,
                new DateTime(2026, 11, 18),
                "rodilla:3"));

        var failingMcpMock = new Mock<IExerciseLibraryMcpClient>();
        failingMcpMock
            .Setup(m => m.GetSubstitutesAsync("rodilla", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Application.Exceptions.TrainingServiceUnavailableException("MCP caído"));

        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<GetDayConflictsQueryHandler>>();
        var handler = new GetDayConflictsQueryHandler(
            trainingClientMock.Object,
            seguimientoClientMock.Object,
            failingMcpMock.Object,
            loggerMock.Object);

        var response = await handler.HandleAsync(new GetDayConflictsQuery(AthleteId));

        Assert.True(response.HasConflicts);
        var conflict = Assert.Single(response.Conflicts);
        Assert.Equal(SquatId, conflict.ExerciseId);
        Assert.Equal("rodilla", conflict.Zone);
        Assert.Empty(conflict.Substitutes);
    }

    // =========================================================================
    // BuscarEjercicios por MCP
    // =========================================================================
    [Fact]
    public async Task Given_McpServerRunning_When_SearchExercisesByNameMuscleAndEquipment_Then_ReturnsMatchingExercisesFromMcp()
    {
        await using var mcpHost = await StartInMemoryMcpServerAsync();
        var client = new ExerciseLibraryMcpClient(
            mcpHost.CreateHttpClient(),
            NullLogger<ExerciseLibraryMcpClient>.Instance);

        var result = await client.SearchExercisesAsync(
            name: "sentadilla",
            muscle: "cuádriceps",
            equipment: "barra con pesas",
            limit: 10);

        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal(SquatId, item.Id);
        Assert.Equal("Sentadilla con barra", item.Name);
        Assert.Equal("Piernas", item.MuscleGroup);
        Assert.Equal("barra con pesas", item.Equipment);
        Assert.Contains("cuádriceps", item.PrimaryMuscles);
        Assert.Equal(McpExerciseType.Weight, item.ExerciseType);
    }

    // =========================================================================
    // ObtenerRestricciones por MCP
    // =========================================================================
    [Fact]
    public async Task Given_McpServerRunning_When_GetRestrictionsForRodilla_Then_ReturnsRestrictionsAndSubstitutesFromMcp()
    {
        await using var mcpHost = await StartInMemoryMcpServerAsync();
        var client = new ExerciseLibraryMcpClient(
            mcpHost.CreateHttpClient(),
            NullLogger<ExerciseLibraryMcpClient>.Instance);

        var result = await client.GetRestrictionsAsync("rodilla");

        Assert.Equal("rodilla", result.Zone);
        var restriction = Assert.Single(result.Restrictions);
        Assert.Equal(SquatId, restriction.ExerciseId);
        Assert.Equal("Sentadilla con barra", restriction.ExerciseName);
        Assert.Equal("cuadriceps", restriction.Zone);
        Assert.Equal(1, restriction.MinPainLevel);
        Assert.Equal("Carga axial sobre rodilla", restriction.Reason);
        var sub = Assert.Single(restriction.Substitutes);
        Assert.Equal(LegPressId, sub.ExerciseId);
        Assert.Equal("Prensa de piernas", sub.Name);
    }

    // =========================================================================
    // Soporte para Agente de IA (KAN-600): ListToolsAsync, CallToolAsync y múltiples llamadas
    // =========================================================================
    [Fact]
    public async Task Given_McpServerRunning_When_ListingToolsAndCallingDynamicallyMultipleTimes_Then_ReusesHttpClientAndReturnsJson()
    {
        await using var mcpHost = await StartInMemoryMcpServerAsync();
        var client = new ExerciseLibraryMcpClient(
            mcpHost.CreateHttpClient(),
            NullLogger<ExerciseLibraryMcpClient>.Instance);

        var tools = await client.ListToolsAsync();
        var toolNames = tools.Select(t => t.Name).ToList();

        Assert.Contains("BuscarEjercicios", toolNames);
        Assert.Contains("ObtenerSustitutos", toolNames);
        Assert.Contains("ObtenerRestricciones", toolNames);
        Assert.All(tools, t => Assert.Equal(JsonValueKind.Object, t.InputSchema.ValueKind));

        // Segunda y tercera llamada sobre la misma instancia de ExerciseLibraryMcpClient (verifica ownsHttpClient: false)
        var rawSubstitutesJson = await client.CallToolAsync(
            "ObtenerSustitutos",
            new Dictionary<string, object?> { ["zona"] = "rodilla" });

        using var doc = JsonDocument.Parse(rawSubstitutesJson);
        Assert.Equal("rodilla", doc.RootElement.GetProperty("zone").GetString());

        var rawSearchJson = await client.CallToolAsync(
            "BuscarEjercicios",
            new Dictionary<string, object?> { ["name"] = "prensa", ["limit"] = 5 });

        using var searchDoc = JsonDocument.Parse(rawSearchJson);
        Assert.True(searchDoc.RootElement.GetProperty("total").GetInt32() >= 1);
    }

    // =========================================================================
    // Reenvío de autorización y manejo de errores
    // =========================================================================
    [Fact]
    public async Task Given_HttpContextWithBearerToken_When_CallingMcpServer_Then_ForwardsAuthorizationHeader()
    {
        string? capturedAuthHeader = null;
        await using var mcpHost = await StartInMemoryMcpServerAsync(ctx =>
        {
            capturedAuthHeader = ctx.Request.Headers.Authorization.ToString();
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer token-jwt-del-pf";
        var accessorMock = new Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        var forwardHandler = new ForwardAuthorizationHandler(accessorMock.Object)
        {
            InnerHandler = mcpHost.CreateHttpMessageHandler()
        };

        var httpClient = new HttpClient(forwardHandler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var client = new ExerciseLibraryMcpClient(httpClient, NullLogger<ExerciseLibraryMcpClient>.Instance);

        await client.GetSubstitutesAsync("rodilla");

        Assert.Equal("Bearer token-jwt-del-pf", capturedAuthHeader);
    }

    [Fact]
    public async Task Given_InvalidArgumentsOrUnknownZone_When_CallingMcpClient_Then_ThrowsDomainExceptionInSpanish()
    {
        await using var mcpHost = await StartInMemoryMcpServerAsync();
        var client = new ExerciseLibraryMcpClient(
            mcpHost.CreateHttpClient(),
            NullLogger<ExerciseLibraryMcpClient>.Instance);

        await Assert.ThrowsAsync<DomainException>(() => client.SearchExercisesAsync());
        await Assert.ThrowsAsync<DomainException>(() => client.GetSubstitutesAsync("  "));
        await Assert.ThrowsAsync<DomainException>(() => client.GetRestrictionsAsync(""));
        await Assert.ThrowsAsync<DomainException>(() => client.CallToolAsync(""));

        // Zona desconocida rechazada por la tool MCP en el servidor
        var ex = await Assert.ThrowsAsync<DomainException>(() => client.GetSubstitutesAsync("zona_inexistente"));
        Assert.Contains("zona", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Given_UnreachableMcpServer_When_CallingMcpClient_Then_ThrowsTrainingServiceUnavailableException()
    {
        var failingHandler = new FailingHttpMessageHandler();
        var httpClient = new HttpClient(failingHandler)
        {
            BaseAddress = new Uri("http://entrenamiento:8080/")
        };

        var client = new ExerciseLibraryMcpClient(httpClient, NullLogger<ExerciseLibraryMcpClient>.Instance);

        await Assert.ThrowsAsync<TrainingServiceUnavailableException>(() =>
            client.GetSubstitutesAsync("rodilla"));

        await Assert.ThrowsAsync<TrainingServiceUnavailableException>(() =>
            client.ListToolsAsync());
    }

    [Theory]
    [InlineData("http://entrenamiento:8080/", "http://entrenamiento:8080/mcp")]
    [InlineData("http://entrenamiento:8080", "http://entrenamiento:8080/mcp")]
    [InlineData("http://gateway:8080/entrenamiento/", "http://gateway:8080/entrenamiento/mcp")]
    [InlineData("http://entrenamiento:8080/mcp", "http://entrenamiento:8080/mcp")]
    public void Given_BaseAddress_When_ResolvingMcpEndpoint_Then_AppendsMcpWithoutDuplication(
        string baseUrl,
        string expectedEndpoint)
    {
        var resolved = ExerciseLibraryMcpClient.ResolveMcpEndpoint(new Uri(baseUrl));
        Assert.Equal(expectedEndpoint, resolved.ToString());
    }

    // =========================================================================
    // Infraestructura de servidor MCP en memoria para las pruebas
    // =========================================================================
    private static async Task<InMemoryMcpServerHost> StartInMemoryMcpServerAsync(
        Action<HttpContext>? onRequest = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<FakeEntrenamientoMcpTools>();

        var app = builder.Build();

        if (onRequest is not null)
        {
            app.Use(async (ctx, next) =>
            {
                onRequest(ctx);
                await next();
            });
        }

        app.MapMcp("/mcp");
        await app.StartAsync();

        return new InMemoryMcpServerHost(app);
    }

    private sealed class InMemoryMcpServerHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient CreateHttpClient() => app.GetTestClient();
        public HttpMessageHandler CreateHttpMessageHandler() => app.GetTestServer().CreateHandler();

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [McpServerToolType]
    private sealed class FakeEntrenamientoMcpTools
    {
        [McpServerTool(
            Name = "BuscarEjercicios",
            Title = "Buscar ejercicios",
            ReadOnly = true,
            OpenWorld = false,
            UseStructuredContent = true)]
        [Description("Busca ejercicios de la biblioteca de ProTraining por nombre, músculo o equipamiento.")]
        public static McpExerciseSearchResultDto SearchExercises(
            string? name = null,
            string? muscle = null,
            string? equipment = null,
            int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(muscle)
                && string.IsNullOrWhiteSpace(equipment))
            {
                throw new McpException("Indicá al menos un filtro: name, muscle o equipment.");
            }

            if (name?.Contains("prensa", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new McpExerciseSearchResultDto(
                    1,
                    [
                        new McpExerciseCatalogItemDto
                        {
                            Id = LegPressId,
                            Name = "Prensa de piernas",
                            MuscleGroup = "Piernas",
                            Equipment = "máquina",
                            PrimaryMuscles = ["cuádriceps"],
                            SecondaryMuscles = ["glúteos"],
                            ExerciseType = McpExerciseType.Weight
                        }
                    ]);
            }

            return new McpExerciseSearchResultDto(
                1,
                [
                    new McpExerciseCatalogItemDto
                    {
                        Id = SquatId,
                        Name = "Sentadilla con barra",
                        MuscleGroup = "Piernas",
                        Equipment = equipment ?? "barra con pesas",
                        PrimaryMuscles = [muscle ?? "cuádriceps"],
                        SecondaryMuscles = ["glúteos"],
                        ExerciseType = McpExerciseType.Weight
                    }
                ]);
        }

        [McpServerTool(
            Name = "ObtenerSustitutos",
            Title = "Obtener sustitutos por zona",
            ReadOnly = true,
            OpenWorld = false,
            UseStructuredContent = true)]
        [Description("Obtiene ejercicios sustitutos seguros para una zona de dolor/molestia.")]
        public static McpZoneSubstitutesResultDto GetSubstitutes(string zona)
        {
            if (string.IsNullOrWhiteSpace(zona)
                || zona.Equals("zona_inexistente", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpException($"La zona '{zona}' no es válida.");
            }

            return new McpZoneSubstitutesResultDto(
                zona,
                [
                    new McpExerciseSubstituteDto(LegPressId, "Prensa de piernas"),
                    new McpExerciseSubstituteDto(HipThrustId, "Empuje de cadera con barra")
                ]);
        }

        [McpServerTool(
            Name = "ObtenerRestricciones",
            Title = "Obtener restricciones de ejercicios por zona",
            ReadOnly = true,
            OpenWorld = false,
            UseStructuredContent = true)]
        [Description("Obtiene las restricciones de ejercicios asociadas a una zona de dolor o molestia.")]
        public static McpZoneRestrictionsResultDto GetRestrictions(string zona)
        {
            if (string.IsNullOrWhiteSpace(zona))
            {
                throw new McpException("Indicá una zona válida.");
            }

            return new McpZoneRestrictionsResultDto(
                zona,
                [
                    new McpRestrictedExerciseItemDto(
                        SquatId,
                        "Sentadilla con barra",
                        "cuadriceps",
                        1,
                        "Carga axial sobre rodilla",
                        [new McpExerciseSubstituteDto(LegPressId, "Prensa de piernas")])
                ]);
        }
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Servidor MCP de Entrenamiento caído.");
        }
    }
}
