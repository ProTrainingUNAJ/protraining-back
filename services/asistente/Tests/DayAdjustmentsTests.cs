using System.Net;
using System.Text;
using System.Text.Json;
using Application.Common.DayAdjustments;
using Application.DTOs.DayAdjustments;
using Application.DTOs.DayConflicts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.DayAdjustments.ApproveDayAdjustment;
using Application.UseCases.DayAdjustments.CreateDayAdjustment;
using Application.UseCases.DayAdjustments.RejectDayAdjustment;
using Application.UseCases.DayConflicts.GetDayConflicts;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
using Infrastructure.Services.Ai.Rules;
using Infrastructure.Services.Seguimiento;
using Infrastructure.Services.Training;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y de integración en memoria para proponer el ajuste del día
/// y pedir la aprobación del PF (#157 · KAN-452).
/// </summary>
public sealed class DayAdjustmentsTests
{
    private static readonly Guid PfId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherPfId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid AthleteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid BoxJumpId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BenchPressId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegPressId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid BandSquatId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static void SetPfId(BaseEntity entity, Guid pfId) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(entity, pfId);

    private sealed class InMemoryTenantDayAdjustmentRepository(List<DayAdjustment> store, Func<Guid> currentPfId)
        : IDayAdjustmentRepository
    {
        public Task AddAsync(DayAdjustment adjustment, CancellationToken cancellationToken = default)
        {
            SetPfId(adjustment, currentPfId());
            store.Add(adjustment);
            return Task.CompletedTask;
        }

        public Task<DayAdjustment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            var match = store.FirstOrDefault(x => x.Id == id && x.PfId == pfId && x.DeletedAt == null);
            return Task.FromResult(match);
        }

        public Task<DayAdjustment?> GetPendingByAthleteAndDateAsync(
            Guid athleteId,
            DateOnly date,
            CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            var match = store.FirstOrDefault(x =>
                x.PfId == pfId &&
                x.AthleteId == athleteId &&
                x.Date == date &&
                x.Status == DayAdjustmentStatus.Pending &&
                x.DeletedAt == null);
            return Task.FromResult(match);
        }
    }

    private sealed class InMemoryTenantPfDecisionRepository(List<PfDecision> store, Func<Guid> currentPfId)
        : IPfDecisionRepository
    {
        public Task AddAsync(PfDecision decision, CancellationToken cancellationToken = default)
        {
            SetPfId(decision, currentPfId());
            store.Add(decision);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<PfDecision> decisions, CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            foreach (var d in decisions)
            {
                SetPfId(d, pfId);
                store.Add(d);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PfDecision>> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            IReadOnlyList<PfDecision> result = store
                .Where(d => d.PfId == pfId && d.SourceId == sourceId && d.DeletedAt == null)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<PfDecision>> GetRecentByAthleteAsync(
            Guid athleteId,
            string? zone = null,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            IReadOnlyList<PfDecision> result = store
                .Where(d => d.PfId == pfId && d.AthleteId == athleteId && (zone == null || d.Zone == zone))
                .Take(limit)
                .ToList();
            return Task.FromResult(result);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static GetDayConflictsQueryHandler CreateConflictsHandlerWithQuadricepsJumpConflict()
    {
        var seguimientoHandler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, $$"""
        {
            "id": "{{Guid.NewGuid()}}",
            "athleteId": "{{AthleteId}}",
            "date": "2026-11-06T00:00:00",
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
                    "date": "2026-11-06",
                    "sessionId": "{{SessionId}}",
                    "exercises": [
                        { "exerciseId": "{{BoxJumpId}}", "name": "Salto al cajón" },
                        { "exerciseId": "{{BenchPressId}}", "name": "Press de banca con barra" }
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
                        "reason": "Impacto reactivo y desaceleración brusca sobre el cuádriceps.",
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

        var seguimientoClient = new SeguimientoClient(
            new HttpClient(seguimientoHandler) { BaseAddress = new Uri("http://seguimiento:8080/") },
            NullLogger<SeguimientoClient>.Instance);

        var trainingClient = new TrainingPlanServiceClient(
            new HttpClient(trainingHandler) { BaseAddress = new Uri("http://entrenamiento:8080/") },
            NullLogger<TrainingPlanServiceClient>.Instance);

        return new GetDayConflictsQueryHandler(trainingClient, seguimientoClient);
    }

    // =========================================================================
    // CRITERIO 3: Ollama y Gemini apagados -> igual sale con justificación por reglas
    // =========================================================================
    [Fact]
    public async Task Given_OllamaAndGeminiOff_When_PfRequestsDayAdjustmentProposal_Then_GeneratesDeterministicRuleProposalAndIsIdempotentForToday()
    {
        var store = new List<DayAdjustment>();
        var repo = new InMemoryTenantDayAdjustmentRepository(store, () => PfId);
        var uowMock = new Mock<IUnitOfWork>();

        var auditMock = new Mock<IAiAuditService>();
        var ollamaHttp = new FakeHttpHandler(_ => throw new HttpRequestException("Ollama apagado"));
        var geminiHttp = new FakeHttpHandler(_ => throw new HttpRequestException("Gemini apagado"));

        var ollamaClient = new OllamaClient(
            new HttpClient(ollamaHttp),
            Options.Create(new OllamaOptions { BaseUrl = "http://localhost:11434", Model = "llama3", TimeoutSeconds = 1 }),
            auditMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(geminiHttp),
            Options.Create(new GeminiOptions { ApiKey = "", Model = "gemini-2.5-flash", TimeoutSeconds = 1 }),
            auditMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(
            auditMock.Object,
            NullLogger<RuleBasedAiClient>.Instance);

        var aiService = new ResilientAiService(
            ollamaClient,
            geminiClient,
            rulesClient,
            NullLogger<ResilientAiService>.Instance);

        var createHandler = new CreateDayAdjustmentCommandHandler(
            repo,
            CreateConflictsHandlerWithQuadricepsJumpConflict(),
            aiService,
            uowMock.Object);

        var response = await createHandler.HandleAsync(new CreateDayAdjustmentCommand(AthleteId));

        Assert.Equal("Pending", response.Status);
        Assert.Equal("Reglas", response.Provider);
        Assert.Equal(SessionId, response.SessionId);
        Assert.Single(response.Items);

        var item = response.Items[0];
        Assert.Equal(BoxJumpId, item.OriginalExerciseId);
        Assert.Equal("Salto al cajón", item.OriginalExerciseName);
        // El sustituto siempre es el primero de la lista de KAN-387
        Assert.Equal(LegPressId, item.SuggestedSubstituteExerciseId);
        Assert.Equal(LegPressId, item.SubstituteExerciseId);
        Assert.Equal("Prensa de piernas", item.SubstituteExerciseName);
        Assert.Equal("cuadriceps", item.Zone);
        Assert.Equal(2, item.PainLevel);
        Assert.Equal(2, item.AvailableSubstitutes.Count);
        Assert.Equal(
            "Se cambia Salto al cajón por Prensa de piernas por molestia en el cuádriceps (nivel 2) reportada hoy.",
            response.Justification);

        // Si ya hay una propuesta Pending para hoy, devuelve esa sin duplicarla
        var secondResponse = await createHandler.HandleAsync(new CreateDayAdjustmentCommand(AthleteId));
        Assert.Equal(response.Id, secondResponse.Id);
        Assert.Single(store);
    }

    // =========================================================================
    // CRITERIO 1: El PF aprueba el ajuste -> aplica en Entrenamiento y guarda PfDecision en la misma transacción
    // =========================================================================
    [Fact]
    public async Task Given_PendingProposal_When_PfApprovesWithDefaultOrAlternativeSubstitute_Then_AppliesInEntrenamientoAndSavesPfDecisionInSameTransaction()
    {
        var adjustmentsStore = new List<DayAdjustment>();
        var decisionsStore = new List<PfDecision>();
        var dayAdjustmentRepo = new InMemoryTenantDayAdjustmentRepository(adjustmentsStore, () => PfId);
        var pfDecisionRepo = new InMemoryTenantPfDecisionRepository(decisionsStore, () => PfId);
        var uowMock = new Mock<IUnitOfWork>();

        var items = new List<DayAdjustmentItemDto>
        {
            new(
                OriginalExerciseId: BoxJumpId,
                OriginalExerciseName: "Salto al cajón",
                SuggestedSubstituteExerciseId: LegPressId,
                SuggestedSubstituteExerciseName: "Prensa de piernas",
                SubstituteExerciseId: LegPressId,
                SubstituteExerciseName: "Prensa de piernas",
                Zone: "cuadriceps",
                PainLevel: 2,
                Reason: "Impacto reactivo.",
                AvailableSubstitutes:
                [
                    new DayConflictSubstituteDto(LegPressId, "Prensa de piernas"),
                    new DayConflictSubstituteDto(BandSquatId, "Sentadillas con bandas elásticas")
                ])
        };

        var proposal = DayAdjustment.Create(
            athleteId: AthleteId,
            date: ArgentinaTime.Today,
            sessionId: SessionId,
            itemsJson: DayAdjustmentMapper.SerializeItems(items),
            justification: "Se cambia Salto al cajón por Prensa de piernas por molestia en el cuádriceps (nivel 2) reportada hoy.",
            provider: "Ollama");

        await dayAdjustmentRepo.AddAsync(proposal);

        var trainingHttp = new FakeHttpHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith($"/api/v1/training-sessions/{SessionId}/athlete-adjustments", req.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, "{}");
        });

        var trainingClient = new TrainingPlanServiceClient(
            new HttpClient(trainingHttp) { BaseAddress = new Uri("http://entrenamiento:8080/") },
            NullLogger<TrainingPlanServiceClient>.Instance);

        var approveHandler = new ApproveDayAdjustmentCommandHandler(
            dayAdjustmentRepo,
            pfDecisionRepo,
            trainingClient,
            uowMock.Object);

        // El PF cambia el sustituto por el segundo de la lista habilitada (Sentadillas con bandas elásticas)
        var approved = await approveHandler.HandleAsync(new ApproveDayAdjustmentCommand(
            proposal.Id,
            new ApproveDayAdjustmentRequestDto(
            [
                new ApproveDayAdjustmentItemInputDto(BoxJumpId, BandSquatId)
            ])));

        Assert.Equal("Approved", approved.Status);
        Assert.Equal(BandSquatId, approved.Items[0].SubstituteExerciseId);
        Assert.Equal("Sentadillas con bandas elásticas", approved.Items[0].SubstituteExerciseName);
        Assert.Contains("Sentadillas con bandas elásticas", approved.Justification);

        // Verificar que llamó a Entrenamiento con el sustituto elegido
        Assert.Single(trainingHttp.Requests);
        Assert.NotNull(trainingHttp.LastRequestBody);
        using var bodyDoc = JsonDocument.Parse(trainingHttp.LastRequestBody!);
        Assert.Equal(AthleteId, bodyDoc.RootElement.GetProperty("athleteId").GetGuid());
        Assert.Equal(BandSquatId, bodyDoc.RootElement.GetProperty("items")[0].GetProperty("substituteExerciseId").GetGuid());

        // Verificar que DayAdjustment y PfDecision se guardaron en UNA SOLA llamada a SaveChangesAsync
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var savedDecisions = await pfDecisionRepo.GetBySourceIdAsync(proposal.Id);
        var decision = Assert.Single(savedDecisions);
        Assert.Equal(PfDecisionSource.DayAdjustment, decision.Source);
        Assert.Equal(PfDecisionType.Modified, decision.Decision);
        Assert.Equal("cuadriceps", decision.Zone);
        Assert.Equal(LegPressId, decision.SuggestedExerciseId);
        Assert.Equal(BandSquatId, decision.FinalExerciseId);
    }

    // =========================================================================
    // CRITERIO 1b: Si el PF aprueba sin cambiar el sustituto -> PfDecision Approved
    // y si intenta elegir un ejercicio fuera de la lista de KAN-387 -> DomainException (400)
    // =========================================================================
    [Fact]
    public async Task Given_PendingProposal_When_PfApprovesInvalidSubstitute_Then_ThrowsDomainException_AndWhenApprovesSuggested_Then_RecordsApprovedDecision()
    {
        var adjustmentsStore = new List<DayAdjustment>();
        var decisionsStore = new List<PfDecision>();
        var dayAdjustmentRepo = new InMemoryTenantDayAdjustmentRepository(adjustmentsStore, () => PfId);
        var pfDecisionRepo = new InMemoryTenantPfDecisionRepository(decisionsStore, () => PfId);
        var uowMock = new Mock<IUnitOfWork>();

        var items = new List<DayAdjustmentItemDto>
        {
            new(
                OriginalExerciseId: BoxJumpId,
                OriginalExerciseName: "Salto al cajón",
                SuggestedSubstituteExerciseId: LegPressId,
                SuggestedSubstituteExerciseName: "Prensa de piernas",
                SubstituteExerciseId: LegPressId,
                SubstituteExerciseName: "Prensa de piernas",
                Zone: "cuadriceps",
                PainLevel: 2,
                Reason: "Impacto reactivo.",
                AvailableSubstitutes:
                [
                    new DayConflictSubstituteDto(LegPressId, "Prensa de piernas")
                ])
        };

        var proposal = DayAdjustment.Create(
            athleteId: AthleteId,
            date: ArgentinaTime.Today,
            sessionId: SessionId,
            itemsJson: DayAdjustmentMapper.SerializeItems(items),
            justification: "Se cambia Salto al cajón por Prensa de piernas.",
            provider: "Reglas");

        await dayAdjustmentRepo.AddAsync(proposal);

        var trainingClientMock = new Mock<ITrainingPlanServiceClient>();
        var approveHandler = new ApproveDayAdjustmentCommandHandler(
            dayAdjustmentRepo,
            pfDecisionRepo,
            trainingClientMock.Object,
            uowMock.Object);

        // Sustituto que no está en la lista de KAN-387 -> DomainException
        await Assert.ThrowsAsync<DomainException>(() =>
            approveHandler.HandleAsync(new ApproveDayAdjustmentCommand(
                proposal.Id,
                new ApproveDayAdjustmentRequestDto(
                [
                    new ApproveDayAdjustmentItemInputDto(BoxJumpId, Guid.NewGuid())
                ]))));

        // Aprobación directa con el sugerido -> Approved
        var result = await approveHandler.HandleAsync(new ApproveDayAdjustmentCommand(proposal.Id));
        Assert.Equal("Approved", result.Status);

        var savedDecisions = await pfDecisionRepo.GetBySourceIdAsync(proposal.Id);
        var decision = Assert.Single(savedDecisions);
        Assert.Equal(PfDecisionType.Approved, decision.Decision);
        Assert.Equal(LegPressId, decision.SuggestedExerciseId);
        Assert.Equal(LegPressId, decision.FinalExerciseId);

        // Intentar volver a aprobar una propuesta ya aprobada -> ConflictException (409)
        await Assert.ThrowsAsync<ConflictException>(() =>
            approveHandler.HandleAsync(new ApproveDayAdjustmentCommand(proposal.Id)));
    }

    // =========================================================================
    // CRITERIO 2: El PF rechaza la propuesta -> no toca Entrenamiento y guarda PfDecision Rejected
    // =========================================================================
    [Fact]
    public async Task Given_PendingProposal_When_PfRejectsWithReason_Then_MarksRejectedDoesNotCallEntrenamientoAndRecordsRejectedDecisionInSameTransaction()
    {
        var adjustmentsStore = new List<DayAdjustment>();
        var decisionsStore = new List<PfDecision>();
        var dayAdjustmentRepo = new InMemoryTenantDayAdjustmentRepository(adjustmentsStore, () => PfId);
        var pfDecisionRepo = new InMemoryTenantPfDecisionRepository(decisionsStore, () => PfId);
        var uowMock = new Mock<IUnitOfWork>();

        var items = new List<DayAdjustmentItemDto>
        {
            new(
                OriginalExerciseId: BoxJumpId,
                OriginalExerciseName: "Salto al cajón",
                SuggestedSubstituteExerciseId: LegPressId,
                SuggestedSubstituteExerciseName: "Prensa de piernas",
                SubstituteExerciseId: LegPressId,
                SubstituteExerciseName: "Prensa de piernas",
                Zone: "cuadriceps",
                PainLevel: 2,
                Reason: "Impacto reactivo.",
                AvailableSubstitutes:
                [
                    new DayConflictSubstituteDto(LegPressId, "Prensa de piernas")
                ])
        };

        var proposal = DayAdjustment.Create(
            athleteId: AthleteId,
            date: ArgentinaTime.Today,
            sessionId: SessionId,
            itemsJson: DayAdjustmentMapper.SerializeItems(items),
            justification: "Se cambia Salto al cajón por Prensa de piernas.",
            provider: "Reglas");

        await dayAdjustmentRepo.AddAsync(proposal);

        var rejectHandler = new RejectDayAdjustmentCommandHandler(
            dayAdjustmentRepo,
            pfDecisionRepo,
            uowMock.Object);

        var rejected = await rejectHandler.HandleAsync(new RejectDayAdjustmentCommand(
            proposal.Id,
            new RejectDayAdjustmentRequestDto("Es molestia leve, que mantenga la sesión original")));

        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("Es molestia leve, que mantenga la sesión original", rejected.RejectionReason);

        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var decisions = await pfDecisionRepo.GetBySourceIdAsync(proposal.Id);
        var decision = Assert.Single(decisions);
        Assert.Equal(PfDecisionType.Rejected, decision.Decision);
        Assert.Equal("cuadriceps", decision.Zone);
        Assert.Equal(LegPressId, decision.SuggestedExerciseId);
        Assert.Equal("Es molestia leve, que mantenga la sesión original", decision.Reason);
    }

    // =========================================================================
    // CRITERIO 4: Otro PF queriendo aprobar o rechazar la propuesta -> recibe 404
    // =========================================================================
    [Fact]
    public async Task Given_ProposalOwnedByPfA_When_PfBTriesToApproveOrReject_Then_ThrowsNotFoundException404()
    {
        var adjustmentsStore = new List<DayAdjustment>();
        var decisionsStore = new List<PfDecision>();
        var currentPfId = PfId;

        var repo = new InMemoryTenantDayAdjustmentRepository(adjustmentsStore, () => currentPfId);
        var pfDecisionRepo = new InMemoryTenantPfDecisionRepository(decisionsStore, () => currentPfId);
        var uowMock = new Mock<IUnitOfWork>();
        var trainingClientMock = new Mock<ITrainingPlanServiceClient>();

        var items = new List<DayAdjustmentItemDto>
        {
            new(
                OriginalExerciseId: BoxJumpId,
                OriginalExerciseName: "Salto al cajón",
                SuggestedSubstituteExerciseId: LegPressId,
                SuggestedSubstituteExerciseName: "Prensa de piernas",
                SubstituteExerciseId: LegPressId,
                SubstituteExerciseName: "Prensa de piernas",
                Zone: "cuadriceps",
                PainLevel: 2,
                Reason: "Impacto reactivo.",
                AvailableSubstitutes: [new DayConflictSubstituteDto(LegPressId, "Prensa de piernas")])
        };

        var proposal = DayAdjustment.Create(
            athleteId: AthleteId,
            date: ArgentinaTime.Today,
            sessionId: SessionId,
            itemsJson: DayAdjustmentMapper.SerializeItems(items),
            justification: "Se cambia Salto al cajón por Prensa de piernas.",
            provider: "Reglas");

        await repo.AddAsync(proposal);

        // Cambiamos el PF activo al PF B
        currentPfId = OtherPfId;

        var approveHandlerPfB = new ApproveDayAdjustmentCommandHandler(
            repo,
            pfDecisionRepo,
            trainingClientMock.Object,
            uowMock.Object);

        var rejectHandlerPfB = new RejectDayAdjustmentCommandHandler(
            repo,
            pfDecisionRepo,
            uowMock.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            approveHandlerPfB.HandleAsync(new ApproveDayAdjustmentCommand(proposal.Id)));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            rejectHandlerPfB.HandleAsync(new RejectDayAdjustmentCommand(proposal.Id)));
    }
}
