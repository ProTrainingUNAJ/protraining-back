using System.Diagnostics;
using System.Text.Json;
using Application.Common.Ai;
using Application.DTOs.Adaptation;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Domain.Entities;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Agent;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Correcciones de la revisión PM del 19/11 sobre la KAN-600: tope de 6 tools, contexto en el prompt del agente,
/// 429 sin espera, solo la primera semana del modelo, configuración de Azure y limpieza del objetivo en texto libre.
/// </summary>
public sealed class AgentRevisionPmTests : IDisposable
{
    private static readonly Guid AthleteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid RestrictedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SubstituteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SquatId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DeadliftId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly Mock<IAiAuditService> _auditMock = new();
    private readonly Mock<IExerciseLibraryMcpClient> _mcpMock = new();

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    // ------------------------------------------------------------------ tope de herramientas

    [Fact]
    public void Given_DefaultAgentOptions_Then_MaxToolCallsIsSixAsRequestedByTheCard()
    {
        Assert.Equal(6, new AgentOptions().MaxToolCalls);
    }

    // ------------------------------------------------------------------ contexto en el prompt del agente

    [Fact]
    public void Given_RestrictionsAndPfDecisions_When_BuildingAgentPromptWithoutCatalog_Then_TextIncludesBoth()
    {
        var request = BuildRequest();

        var prompt = AgentPromptBuilder.BuildPlanDraftUserPrompt(request);

        Assert.Contains("isquiotibiales", prompt);
        Assert.Contains(RestrictedId.ToString(), prompt);
        Assert.Contains("Puente de gluteos", prompt);
        Assert.Contains("DECISIONES PREVIAS DEL PREPARADOR FÍSICO", prompt);
        Assert.Contains("decisión: Modified", prompt);
        Assert.Contains(SubstituteId.ToString(), prompt);
        Assert.DoesNotContain("Desgarro leve", prompt);
    }

    [Fact]
    public void Given_RestrictionsAndPfDecisions_When_BuildingAgentPromptWithCatalog_Then_UsesBlockFormatWithContext()
    {
        var request = BuildRequest(goal: "fuerza de tren inferior");
        IReadOnlyList<CatalogExerciseDto> catalog =
        [
            new(SquatId, "Sentadilla", "Piernas", "Barra"),
            new(DeadliftId, "Peso muerto rumano", "Piernas", "Barra")
        ];

        var prompt = AgentPromptBuilder.BuildPlanDraftUserPrompt(request, null, catalog);

        // Restricciones y decisiones del PF (KAN-468)
        Assert.Contains(RestrictedId.ToString(), prompt);
        Assert.Contains("Puente de gluteos", prompt);
        Assert.Contains("DECISIONES PREVIAS DEL PREPARADOR FÍSICO", prompt);
        // Directivas deportivas (KAN-591)
        Assert.Contains("ENFOQUE MUSCULAR DEL OBJETIVO: Piernas", prompt);
        // Formato por bloques con número de la lista (KAN-589)
        Assert.Contains("warmUp", prompt);
        Assert.Contains("strengthA", prompt);
        Assert.Contains("exerciseIndex", prompt);
        Assert.Contains("1. Sentadilla | grupo: Piernas", prompt);
        Assert.Contains("2. Peso muerto rumano | grupo: Piernas", prompt);
    }

    [Fact]
    public void Given_CategoryRequest_When_BuildingAgentPrompt_Then_StatesTheCategoryAnonymously()
    {
        var request = new CreatePlanDraftRequestDto(Goal: "Fuerza", CategoryId: CategoryId, Weeks: 4, DaysPerWeek: 3);

        var prompt = AgentPromptBuilder.BuildPlanDraftUserPrompt(request);

        Assert.Contains("categoría completa", prompt);
        Assert.Contains(CategoryId.ToString(), prompt);
    }

    // ------------------------------------------------------------------ limpieza del objetivo en texto libre

    [Fact]
    public void Given_GoalWithEmailAndPhone_When_BuildingAgentPrompt_Then_ContactDataIsRemoved()
    {
        var request = new CreatePlanDraftRequestDto(
            Goal: "Fuerza, contacto juan.perez@gmail.com o 11 5555-1234",
            AthleteId: AthleteId);

        var prompt = AgentPromptBuilder.BuildPlanDraftUserPrompt(request);

        Assert.DoesNotContain("@", prompt);
        Assert.DoesNotContain("juan.perez", prompt);
        Assert.DoesNotContain("5555", prompt);
        Assert.Contains(PromptSanitizer.Placeholder, prompt);
    }

    [Theory]
    [InlineData("Hipertrofia 4 series de 8 al 75% desde 2026-11-20")]
    [InlineData("Plancha Copenhagen y Curl Nórdico el 20/11/2026")]
    [InlineData("Press 3x10 al 70-80% de 1RM")]
    public void Given_GoalWithoutContactData_When_Sanitizing_Then_RemainsUntouched(string goal)
    {
        Assert.Equal(goal, PromptSanitizer.RedactContactData(goal));
    }

    [Theory]
    [InlineData("llamame al +54 9 11 2345-6789 por la rutina")]
    [InlineData("mi celu es (011) 4567-8901")]
    public void Given_GoalWithPhone_When_Sanitizing_Then_PhoneIsReplaced(string goal)
    {
        var sanitized = PromptSanitizer.RedactContactData(goal);

        Assert.Contains(PromptSanitizer.Placeholder, sanitized);
        Assert.DoesNotContain("2345", sanitized);
        Assert.DoesNotContain("4567", sanitized);
    }

    // ------------------------------------------------------------------ creación del borrador con contexto

    [Fact]
    public async Task Given_AthleteDraft_When_Creating_Then_AdaptationContextTravelsToTheAiService()
    {
        var context = BuildContext();
        var contextMock = new Mock<IAthleteAdaptationContextService>();
        contextMock.Setup(s => s.GetAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(context);

        var (handler, getCaptured) = BuildHandler(contextMock.Object);

        await handler.HandleAsync(new CreatePlanDraftCommand(new CreatePlanDraftRequestDto(
            Goal: "Fuerza", AthleteId: AthleteId, Weeks: 1, DaysPerWeek: 3)));

        var captured = getCaptured();
        Assert.NotNull(captured);
        Assert.Same(context, captured!.AdaptationContext);
    }

    [Fact]
    public async Task Given_ContextServiceDown_When_Creating_Then_DraftIsStillGeneratedWithoutContext()
    {
        var contextMock = new Mock<IAthleteAdaptationContextService>();
        contextMock.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Seguimiento caído"));

        var (handler, getCaptured) = BuildHandler(contextMock.Object);

        var response = await handler.HandleAsync(new CreatePlanDraftCommand(new CreatePlanDraftRequestDto(
            Goal: "Fuerza", AthleteId: AthleteId, Weeks: 1, DaysPerWeek: 3)));

        Assert.NotNull(response);
        Assert.Null(getCaptured()!.AdaptationContext);
    }

    // ------------------------------------------------------------------ catálogo numerado + bloques en el agente

    [Fact]
    public async Task Given_RestrictedExercise_When_AgentGenerates_Then_CatalogHidesItAddsSubstituteAndBlocksAreExpanded()
    {
        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock
            .Setup(c => c.GetExercisesForGoalAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogExerciseDto>
            {
                new(RestrictedId, "Curl Nordico", "Piernas", "Peso corporal"),
                new(SquatId, "Sentadilla", "Piernas", "Barra"),
                new(DeadliftId, "Peso muerto rumano", "Piernas", "Barra")
            });
        catalogMock
            .Setup(c => c.FindByIdAsync(SubstituteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogExerciseDto(SubstituteId, "Puente de gluteos", "Piernas", "Peso corporal"));

        // Catálogo que ve el modelo: [Sentadilla (1), Peso muerto rumano (2), Puente de gluteos (3)]
        var blockJson = JsonSerializer.Serialize(new
        {
            name = "Plan tren inferior",
            objective = "Fuerza",
            justificationSummary = "Sin ejercicios restringidos.",
            modelSessions = new[]
            {
                new
                {
                    name = "Sesion 1",
                    dayOfWeek = "Monday",
                    order = 1,
                    coachNotes = "Tecnica",
                    warmUp = new[] { new { exerciseIndex = 3, sets = 2, reps = 10 } },
                    strengthA = new[] { new { exerciseIndex = 1, sets = 4, reps = 6, targetPercentage = 80m } },
                    strengthB = new[] { new { exerciseIndex = 2, sets = 3, reps = 8, targetPercentage = 75m } },
                    accessories = Array.Empty<object>()
                }
            }
        });

        _server.Given(Request.Create().UsingPost().WithPath("/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(ChatResponse(blockJson)));

        var client = new GenericAgentAiClient(
            ProviderOptions("OpenAI", "/chat/completions"),
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance,
            catalogMock.Object);

        var request = BuildRequest(goal: "fuerza de tren inferior") with { Weeks = 2, DaysPerWeek = 1 };

        var result = await client.TryGeneratePlanDraftAsync(request);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Weeks.Count);
        Assert.Equal(
            new[] { SubstituteId, SquatId, DeadliftId },
            result.Weeks[0].Sessions[0].Exercises.Select(e => e.ExerciseId!.Value).ToArray());

        var body = _server.LogEntries.First().RequestMessage.Body ?? string.Empty;
        Assert.Contains("exerciseIndex", body);
        Assert.Contains("Sentadilla", body);
        Assert.Contains("Puente de gluteos", body);
        Assert.DoesNotContain("Curl Nordico", body);
        Assert.Contains(RestrictedId.ToString(), body); // figura solo como restricción, no en el catálogo
    }

    // ------------------------------------------------------------------ 429: pasar de inmediato al siguiente proveedor

    [Fact]
    public async Task Given_Provider429_When_Generating_Then_ReturnsNullAtOnceWithoutWaitingOrRetrying()
    {
        _server.Given(Request.Create().UsingPost().WithPath("/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(429)
                .WithBody("{\"message\":\"Token limit is exceeded. Try again in 25 seconds.\"}"));

        var client = new GenericAgentAiClient(
            ProviderOptions("OpenAI", "/chat/completions"),
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var stopwatch = Stopwatch.StartNew();
        var result = await client.TryGeneratePlanDraftAsync(BuildRequest(goal: "Fuerza") with { Weeks = 1, DaysPerWeek = 1 });
        stopwatch.Stop();

        Assert.Null(result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Tardó {stopwatch.Elapsed.TotalSeconds:0.0} s: no debe esperar ante un 429.");
        Assert.Single(_server.LogEntries);
    }

    [Fact]
    public async Task Given_FirstProviderRateLimited_When_ChainRuns_Then_NextProviderAnswersImmediately()
    {
        _server.Given(Request.Create().UsingPost().WithPath("/openai/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(429).WithBody("{\"message\":\"Rate limit reached. Try again in 30 seconds.\"}"));
        _server.Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody(ChatResponse(ClassicPlanJson("Plan Gemini", 80m, 80m, 80m))));

        var openAi = new GenericAgentAiClient(
            ProviderOptions("OpenAI", "/openai/chat/completions"), new HttpClient(), _mcpMock.Object, _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);
        var gemini = new GenericAgentAiClient(
            ProviderOptions("Gemini", "/gemini/chat/completions"), new HttpClient(), _mcpMock.Object, _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);
        var rules = new RuleBasedAiClient(_auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var resilient = new ResilientAiService(
            [openAi, gemini, rules],
            semanticValidator: null,
            responseCache: null,
            NullLogger<ResilientAiService>.Instance);

        var stopwatch = Stopwatch.StartNew();
        var (content, provider) = await resilient.GeneratePlanDraftAsync(
            BuildRequest(goal: "Fuerza") with { Weeks = 1, DaysPerWeek = 1 });
        stopwatch.Stop();

        Assert.Equal("Agente (Gemini)", provider);
        Assert.Equal("Plan Gemini", content.Name);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"La cadena tardó {stopwatch.Elapsed.TotalSeconds:0.0} s.");
        Assert.Single(_server.LogEntries, e => e.RequestMessage.Path == "/openai/chat/completions");
    }

    // ------------------------------------------------------------------ varias semanas del modelo

    [Fact]
    public async Task Given_ModelReturnsThreeWeeks_When_OneWeekRequested_Then_OnlyTheFirstIsKept()
    {
        _server.Given(Request.Create().UsingPost().WithPath("/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody(ChatResponse(ClassicPlanJson("Plan largo", 80m, 90m, 95m))));

        var result = await GenerateClassicAsync(weeks: 1);

        Assert.NotNull(result);
        Assert.Single(result!.Weeks);
        Assert.Equal(1, result.Weeks[0].WeekNumber);
        Assert.Equal(80m, result.Weeks[0].Sessions[0].Exercises[0].TargetPercentage);
    }

    [Fact]
    public async Task Given_ModelReturnsThreeWeeks_When_TwoWeeksRequested_Then_SecondWeekComesFromTheProgressionEngine()
    {
        _server.Given(Request.Create().UsingPost().WithPath("/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody(ChatResponse(ClassicPlanJson("Plan largo", 80m, 99m, 99m))));

        var result = await GenerateClassicAsync(weeks: 2);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Weeks.Count);
        Assert.Equal(80m, result.Weeks[0].Sessions[0].Exercises[0].TargetPercentage);
        // La regla decide la progresión: semana 2 = +2,5 % sobre la base, no el 99 % que inventó el modelo
        Assert.Equal(82.5m, result.Weeks[1].Sessions[0].Exercises[0].TargetPercentage);
    }

    // ------------------------------------------------------------------ configuración del gateway de Azure

    [Fact]
    public void Given_AzureGatewayWithoutDeployment_When_ResolvingUri_Then_UsesConfiguredModelAndApiVersion()
    {
        var uri = GenericAgentAiClient.ResolveChatCompletionsUri(
            "https://rs-ai-gateway-unaj.azure-api.net/unaj-llm", "gpt-5-mini", "2025-04-01");

        Assert.Equal(
            "https://rs-ai-gateway-unaj.azure-api.net/unaj-llm/openai/deployments/gpt-5-mini/chat/completions?api-version=2025-04-01",
            uri.ToString());
    }

    [Fact]
    public void Given_AzureGatewayWithoutConfiguration_When_ResolvingUri_Then_KeepsTheHistoricalDefaults()
    {
        var uri = GenericAgentAiClient.ResolveChatCompletionsUri("https://rs-ai-gateway-unaj.azure-api.net/unaj-llm");

        Assert.Contains("/openai/deployments/gpt-4.1-mini/chat/completions", uri.ToString());
        Assert.Contains("api-version=2025-03-01-preview", uri.ToString());
    }

    [Fact]
    public void Given_OpenAiCompatibleBaseUrl_When_ResolvingUri_Then_AppendsChatCompletionsOnly()
    {
        var uri = GenericAgentAiClient.ResolveChatCompletionsUri("https://generativelanguage.googleapis.com/v1beta/openai/", "gemini-3.8-flash", "2025-04-01");

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", uri.ToString());
    }

    // ------------------------------------------------------------------ helpers

    private async Task<PlanDraftContentDto?> GenerateClassicAsync(int weeks)
    {
        var client = new GenericAgentAiClient(
            ProviderOptions("OpenAI", "/chat/completions"),
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        return await client.TryGeneratePlanDraftAsync(
            BuildRequest(goal: "Fuerza") with { Weeks = weeks, DaysPerWeek = 1, StartDate = new DateOnly(2026, 11, 23) });
    }

    private AgentProviderOptions ProviderOptions(string name, string path) => new()
    {
        Name = name,
        BaseUrl = $"{_server.Url}{path}",
        Model = "modelo-de-prueba",
        ApiKey = "clave-de-prueba",
        TimeoutSeconds = 10
    };

    private static CreatePlanDraftRequestDto BuildRequest(string goal = "Fuerza") => new(
        Goal: goal,
        AthleteId: AthleteId,
        Weeks: 4,
        DaysPerWeek: 3,
        PreviousPfDecisions:
        [
            new PfDecisionContextDto(
                PfDecisionSource.PlanDraft,
                new DateOnly(2026, 11, 14),
                "isquiotibiales",
                RestrictedId,
                PfDecisionType.Modified,
                SubstituteId,
                60m)
        ],
        AdaptationContext: BuildContext());

    private static AthleteAdaptationContextDto BuildContext() => new(
        AthleteId,
        [
            new ActiveInjuryRemoteDto(
                Guid.NewGuid(), AthleteId, "isquiotibiales", "Desgarro leve",
                DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(10), "Active", null, DateTime.UtcNow.AddDays(-5))
        ],
        [],
        [
            new ExerciseRestrictionContextDto(
                RestrictedId, "isquiotibiales", "Alta carga excéntrica",
                [new AuthorizedSubstituteDto(SubstituteId, "Puente de gluteos")])
        ]);

    private static (CreatePlanDraftCommandHandler Handler, Func<CreatePlanDraftRequestDto?> Captured) BuildHandler(
        IAthleteAdaptationContextService contextService)
    {
        CreatePlanDraftRequestDto? captured = null;
        var content = new PlanDraftContentDto(
            "Plan", "Objetivo", "Justificación",
            [new PlanDraftWeekDto(1, [new PlanDraftSessionDto("Sesión 1", DayOfWeek.Monday, 1, null, [new PlanDraftExerciseDto(Guid.NewGuid(), 1, 3, 10)])])]);

        var aiMock = new Mock<IAiService>();
        aiMock
            .Setup(a => a.GeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<IReadOnlyList<DateOnly>?>(), It.IsAny<CancellationToken>()))
            .Callback<CreatePlanDraftRequestDto, IReadOnlyList<DateOnly>?, CancellationToken>((r, _, _) => captured = r)
            .ReturnsAsync((content, "Reglas"));

        var handler = new CreatePlanDraftCommandHandler(
            aiMock.Object,
            new Mock<IPlanDraftRepository>().Object,
            new Mock<IUnitOfWork>().Object,
            adaptationContextService: contextService);

        return (handler, () => captured);
    }

    private static string ChatResponse(string content) => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-test",
        choices = new[]
        {
            new { index = 0, message = new { role = "assistant", content }, finish_reason = "stop" }
        },
        usage = new { prompt_tokens = 50, completion_tokens = 50, total_tokens = 100 }
    });

    private static string ClassicPlanJson(string name, decimal week1, decimal week2, decimal week3)
    {
        object Week(int number, decimal percentage) => new
        {
            weekNumber = number,
            sessions = new[]
            {
                new
                {
                    name = "Sesión 1",
                    dayOfWeek = "Monday",
                    order = 1,
                    coachNotes = "Notas",
                    exercises = new[]
                    {
                        new
                        {
                            exerciseId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                            exerciseName = "Sentadilla",
                            order = 1,
                            sets = 4,
                            reps = 6,
                            targetPercentage = percentage
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(new
        {
            name,
            objective = "Objetivo deportivo",
            justificationSummary = "Resumen técnico.",
            weeks = new[] { Week(1, week1), Week(2, week2), Week(3, week3) }
        });
    }
}
