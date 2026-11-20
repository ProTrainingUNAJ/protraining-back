using API.Controllers;
using Application.Common.Helpers;
using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.ApproveRecommendation;
using Application.UseCases.Recommendations.DiscardRecommendation;
using Application.UseCases.Recommendations.GenerateAthleteRecommendations;
using Application.UseCases.Recommendations.GenerateCategoryRecommendations;
using Application.UseCases.Recommendations.GetRecommendationById;
using Application.UseCases.Recommendations.GetRecommendations;
using Application.UseCases.Recommendations.UpdateRecommendation;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y de casos de uso para el motor de recomendaciones (#196 · KAN-491).
/// </summary>
public sealed class RecommendationEngineTests
{
    private static readonly Guid PfId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AthleteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAthleteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CategoryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static void SetPfId(BaseEntity entity, Guid pfId) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(entity, pfId);

    private sealed class InMemoryTenantRecommendationRepository(
        List<AthleteRecommendation> store,
        Func<Guid> currentPfId) : IAthleteRecommendationRepository
    {
        public Task AddAsync(AthleteRecommendation recommendation, CancellationToken cancellationToken = default)
        {
            SetPfId(recommendation, currentPfId());
            store.Add(recommendation);
            return Task.CompletedTask;
        }

        public Task<AthleteRecommendation?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            var match = store.FirstOrDefault(r => r.Id == id && r.PfId == pfId && r.DeletedAt == null);
            return Task.FromResult(match);
        }

        public Task<AthleteRecommendation?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
            GetByIdAsNoTrackingAsync(id, cancellationToken);

        public Task<IReadOnlyList<AthleteRecommendation>> ListAsync(
            Guid? athleteId = null,
            Guid? categoryId = null,
            RecommendationStatus? status = null,
            CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            var query = store.Where(r => r.PfId == pfId && r.DeletedAt == null);

            if (athleteId.HasValue && athleteId.Value != Guid.Empty)
                query = query.Where(r => r.AthleteId == athleteId.Value);

            if (categoryId.HasValue && categoryId.Value != Guid.Empty)
                query = query.Where(r => r.CategoryId == categoryId.Value);

            if (status.HasValue)
                query = query.Where(r => r.Status == status.Value);

            IReadOnlyList<AthleteRecommendation> result = query
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<AthleteRecommendation>> ListApprovedByAthleteAsync(
            Guid athleteId,
            CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            IReadOnlyList<AthleteRecommendation> result = store
                .Where(r => r.PfId == pfId && r.DeletedAt == null && r.AthleteId == athleteId && r.Status == RecommendationStatus.Approved)
                .OrderByDescending(r => r.ReviewedAtUtc ?? r.UpdatedAt)
                .ThenByDescending(r => r.CreatedAt)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<AthleteRecommendation>> ListByAthleteIdsAndStatusesAsync(
            IReadOnlyCollection<Guid> athleteIds,
            IReadOnlyCollection<RecommendationStatus> statuses,
            CancellationToken cancellationToken = default)
        {
            if (athleteIds.Count == 0 || statuses.Count == 0)
                return Task.FromResult<IReadOnlyList<AthleteRecommendation>>([]);

            var pfId = currentPfId();
            var athleteSet = athleteIds.ToHashSet();
            var statusSet = statuses.ToHashSet();
            IReadOnlyList<AthleteRecommendation> result = store
                .Where(r => r.PfId == pfId &&
                            r.DeletedAt == null &&
                            athleteSet.Contains(r.AthleteId) &&
                            statusSet.Contains(r.Status))
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return Task.FromResult(result);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }

    [Fact]
    public void Analyze_GivenAthleteLowInJumpComparedToGroup_ReturnsJumpBelowAverageSignal()
    {
        var athleteTests = new List<AthleteFitnessTestRemoteDto>
        {
            new(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 34.0m, "cm", 1, new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc))
        };

        var averages = new CategoryAveragesRemoteDto(
            CategoryId,
            10,
            new DateOnly(2026, 11, 9),
            new DateOnly(2026, 11, 15),
            [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, 10)],
            []);

        var result = AthleteProfileAnalyzer.Analyze(athleteTests, averages, weeklyTrends: null);

        var signal = Assert.Single(result.Signals);
        Assert.Equal("salto por debajo del promedio", signal.Reason);
        var metric = Assert.Single(signal.Metrics);
        Assert.Equal(-19.0, metric.PercentageDifference);
        Assert.Contains("Por debajo del promedio", metric.Trend);
    }

    [Fact]
    public void Analyze_GivenLowerIsBetterTestSlowerThanGroup_ReturnsSpeedBelowAverageSignal()
    {
        var athleteTests = new List<AthleteFitnessTestRemoteDto>
        {
            new(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Carrera 30 m", "Velocity", 4.8m, "s", 1, new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc))
        };

        var averages = new CategoryAveragesRemoteDto(
            CategoryId,
            10,
            new DateOnly(2026, 11, 9),
            new DateOnly(2026, 11, 15),
            [new FitnessTestAverageRemoteDto("Carrera 30 m", "s", 4.2, 10)],
            []);

        var result = AthleteProfileAnalyzer.Analyze(athleteTests, averages, weeklyTrends: null);

        var signal = Assert.Single(result.Signals);
        Assert.Equal("velocidad por debajo del promedio", signal.Reason);
        var metric = Assert.Single(signal.Metrics);
        Assert.Equal(-14.3, metric.PercentageDifference);
    }

    [Fact]
    public void Analyze_GivenPhysicalGapAndLoadSpikeWithWellnessDrop_EnrichesGapAndAddsTrendSignal()
    {
        var athleteTests = new List<AthleteFitnessTestRemoteDto>
        {
            new(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 35.0m, "cm", 1, new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc))
        };

        var averages = new CategoryAveragesRemoteDto(
            CategoryId,
            8,
            new DateOnly(2026, 11, 9),
            new DateOnly(2026, 11, 15),
            [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, 8)],
            []);

        var trends = new AthleteWeeklyTrendsRemoteDto(
            AthleteId,
            new DateOnly(2026, 10, 20),
            new DateOnly(2026, 11, 16),
            [
                new(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 26), 4.2, 900, 180, 4, 3),
                new(new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 2), 4.0, 1000, 200, 4, 3),
                new(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 9), 3.9, 1000, 200, 4, 3),
                new(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16), 2.7, 1500, 260, 5, 4)
            ]);

        var result = AthleteProfileAnalyzer.Analyze(athleteTests, averages, trends);

        Assert.Equal(2, result.Signals.Count);
        Assert.Equal("salto por debajo del promedio", result.Signals[0].Reason);
        Assert.Contains("aumento de carga", result.Signals[0].SuggestedFocus);
        Assert.NotNull(result.Signals[0].Trends);
        Assert.Equal("aumento de carga con caída en el bienestar semanal", result.Signals[1].Reason);
    }

    [Fact]
    public void Analyze_GivenPureNegativeWellnessTrendWithoutPhysicalGaps_ReturnsTrendSignal()
    {
        var trends = new AthleteWeeklyTrendsRemoteDto(
            AthleteId,
            new DateOnly(2026, 10, 20),
            new DateOnly(2026, 11, 16),
            [
                new(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 26), 4.1, 1000, 180, 4, 3),
                new(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16), 3.1, 1050, 190, 4, 3)
            ]);

        var result = AthleteProfileAnalyzer.Analyze([], categoryAverages: null, trends);

        var signal = Assert.Single(result.Signals);
        Assert.Equal("tendencia a la baja en bienestar semanal", signal.Reason);
        Assert.Empty(signal.Metrics);
        Assert.NotNull(signal.Trends);
    }

    [Fact]
    public async Task GenerateForAthlete_GivenLowJumpAgainstGroup_PersistsPendingRecommendationWithoutPii()
    {
        // Criterio de aceptación KAN-491:
        // DADO un atleta bajo en salto frente a su grupo CUANDO corre el motor ENTONCES queda una recomendación pendiente con el motivo «salto por debajo del promedio».
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        structureMock.SetupHabitsPermission(AthleteId);
        structureMock.Setup(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryDto(CategoryId, "Primera División")]);

        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 34.0m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId,
                10,
                new DateOnly(2026, 11, 9),
                new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, 10)],
                []));
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(AthleteId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteWeeklyTrendsRemoteDto(
                AthleteId,
                new DateOnly(2026, 10, 20),
                new DateOnly(2026, 11, 16),
                [new WeeklyTrendPointRemoteDto(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16), 4.0, 1100, 180, 4, 3)]));

        TrainingRecommendationRequestDto? capturedAiRequest = null;
        var aiMock = new Mock<IAiService>();
        aiMock.Setup(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<TrainingRecommendationRequestDto, CancellationToken>((req, _) => capturedAiRequest = req)
            .ReturnsAsync((TrainingRecommendationRequestDto req, CancellationToken _) =>
                new TrainingRecommendationResponseDto(
                    Title: "Refuerzo de potencia en salto",
                    Reason: req.Reason,
                    RecommendationText: "Sumá dos bloques semanales de pliometría de bajo impacto y saltos reactivos.",
                    ProviderUsed: "Agente (OpenAI)"));

        var handler = new GenerateAthleteRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow);

        var response = await handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));

        Assert.Equal(1, response.GeneratedCount);
        Assert.Equal(0, response.ExistingPendingCount);
        Assert.Equal(1, uow.SaveChangesCount);

        var saved = Assert.Single(store);
        Assert.Equal(RecommendationStatus.Pending, saved.Status);
        Assert.Equal("salto por debajo del promedio", saved.Reason);
        Assert.Equal("Agente (OpenAI)", saved.Provider);
        Assert.Equal(CategoryId, saved.CategoryId);
        Assert.Equal("Primera División", saved.CategoryName);

        var item = Assert.Single(response.Recommendations);
        Assert.Equal("Pending", item.Status);
        Assert.Equal("salto por debajo del promedio", item.Reason);
        Assert.Single(item.Metrics);
        Assert.NotNull(item.Trends);

        // Cero PII en la solicitud a la IA
        Assert.NotNull(capturedAiRequest);
        Assert.Equal("salto por debajo del promedio", capturedAiRequest!.Reason);
        Assert.Equal("Primera División", capturedAiRequest.CategoryName);

        // Segunda corrida mientras sigue Pending: idempotente, no duplica ni vuelve a llamar a la IA
        var secondRun = await handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));
        Assert.Equal(0, secondRun.GeneratedCount);
        Assert.Equal(1, secondRun.ExistingPendingCount);
        Assert.Single(store);
        aiMock.Verify(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateForAthlete_GivenAthleteAboveAverage_DoesNotCallAiOrPersist()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        structureMock.SetupHabitsPermission(AthleteId);
        structureMock.Setup(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryDto(CategoryId, "Primera División")]);

        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 46.0m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId,
                10,
                new DateOnly(2026, 11, 9),
                new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, 10)],
                []));
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(AthleteId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteWeeklyTrendsRemoteDto(
                AthleteId,
                new DateOnly(2026, 10, 20),
                new DateOnly(2026, 11, 16),
                [new WeeklyTrendPointRemoteDto(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16), 4.2, 1000, 180, 4, 3)]));

        var aiMock = new Mock<IAiService>();

        var handler = new GenerateAthleteRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow);

        var response = await handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));

        Assert.Equal(0, response.GeneratedCount);
        Assert.Equal(0, response.ExistingPendingCount);
        Assert.Empty(response.Recommendations);
        Assert.Empty(store);
        Assert.Equal(0, uow.SaveChangesCount);
        aiMock.Verify(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateForCategory_QueriesCategoryAveragesOnce_AndGeneratesOnlyForAthletesWithSignals()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.GetCategoryAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureCategoryDto(CategoryId, "Reserva"));
        structureMock.Setup(s => s.GetCategoryAthletesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StructureCategoryAthleteDto(AthleteId, true),
                new StructureCategoryAthleteDto(SecondAthleteId, true)
            ]);
        structureMock.SetupHabitsPermission(AthleteId);
        structureMock.SetupHabitsPermission(SecondAthleteId);

        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId,
                8,
                new DateOnly(2026, 11, 9),
                new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, 8)],
                []));

        // Atleta 1 bajo en salto, Atleta 2 por encima
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 34.0m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(SecondAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), SecondAthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 45.0m, "cm", 1, DateTime.UtcNow)
            ]);

        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(It.IsAny<Guid>(), 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, int _, CancellationToken _) => new AthleteWeeklyTrendsRemoteDto(
                id,
                new DateOnly(2026, 10, 20),
                new DateOnly(2026, 11, 16),
                [new WeeklyTrendPointRemoteDto(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 16), 4.0, 1000, 180, 4, 3)]));

        var aiMock = new Mock<IAiService>();
        aiMock.Setup(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingRecommendationRequestDto req, CancellationToken _) =>
                RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(req, "Reglas"));

        var handler = new GenerateCategoryRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow);

        var response = await handler.HandleAsync(new GenerateCategoryRecommendationsCommand(CategoryId));

        Assert.Equal("Reserva", response.CategoryName);
        Assert.Equal(2, response.EvaluatedAthletes);
        Assert.Equal(1, response.GeneratedCount);
        Assert.Equal(1, uow.SaveChangesCount);

        var rec = Assert.Single(store);
        Assert.Equal(AthleteId, rec.AthleteId);
        Assert.Equal("salto por debajo del promedio", rec.Reason);
        seguimientoMock.Verify(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateForAthlete_WhenUnknownAthleteOrInvalidWeeks_ThrowsExpectedExceptions()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new GenerateAthleteRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            new Mock<ISeguimientoClient>().Object,
            repository,
            new Mock<IAiService>().Object,
            uow);

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId, Weeks: 0)));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId)));
    }

    [Fact]
    public async Task GetRecommendationsAndById_FilterAndValidateStatusAndDomainTransitions()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);

        var pending = AthleteRecommendation.Create(
            AthleteId,
            "Título",
            "salto por debajo del promedio",
            "Texto de recomendación");
        await repository.AddAsync(pending);

        var approved = AthleteRecommendation.Create(
            SecondAthleteId,
            "Título 2",
            "fuerza por debajo del promedio",
            "Texto 2");
        approved.Approve();
        await repository.AddAsync(approved);

        Assert.Throws<DomainException>(() => approved.UpdateContent("Nuevo", "Texto"));
        Assert.Throws<DomainException>(() => approved.Discard());

        var listHandler = new GetRecommendationsQueryHandler(repository);
        var onlyPending = await listHandler.HandleAsync(new GetRecommendationsQuery(Status: "Pending"));
        Assert.Single(onlyPending);
        Assert.Equal(pending.Id, onlyPending[0].Id);

        await Assert.ThrowsAsync<DomainException>(() =>
            listHandler.HandleAsync(new GetRecommendationsQuery(Status: "Invalido")));

        var getByIdHandler = new GetRecommendationByIdQueryHandler(repository);
        var found = await getByIdHandler.HandleAsync(new GetRecommendationByIdQuery(pending.Id));
        Assert.Equal("salto por debajo del promedio", found.Reason);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            getByIdHandler.HandleAsync(new GetRecommendationByIdQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task Generate_WhenDependentServiceUnavailable_Propagates503ExceptionsWithoutSaving()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureDown = new Mock<IStructureAthleteClient>();
        structureDown.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureServiceUnavailableException("Estructura no disponible."));
        structureDown.SetupHabitsPermission(AthleteId);

        var handlerStructureDown = new GenerateAthleteRecommendationsCommandHandler(
            structureDown.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            new Mock<ISeguimientoClient>().Object,
            repository,
            new Mock<IAiService>().Object,
            uow);

        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            handlerStructureDown.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId)));

        var structureOk = new Mock<IStructureAthleteClient>();
        structureOk.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        structureOk.Setup(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryDto(CategoryId, "Primera")]);
        structureOk.SetupHabitsPermission(AthleteId);

        var seguimientoDown = new Mock<ISeguimientoClient>();
        seguimientoDown.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SeguimientoServiceUnavailableException("Seguimiento no disponible."));
        seguimientoDown.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CategoryAveragesRemoteDto?)null);
        seguimientoDown.Setup(s => s.GetAthleteWeeklyTrendsAsync(AthleteId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var handlerSeguimientoDown = new GenerateAthleteRecommendationsCommandHandler(
            structureOk.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoDown.Object,
            repository,
            new Mock<IAiService>().Object,
            uow);

        await Assert.ThrowsAsync<SeguimientoServiceUnavailableException>(() =>
            handlerSeguimientoDown.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId)));

        Assert.Empty(store);
        Assert.Equal(0, uow.SaveChangesCount);
    }

    [Fact]
    public async Task Controller_Returns201WhenGenerated_And200WhenNoNewRecommendations()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        structureMock.SetupHabitsPermission(AthleteId);
        structureMock.Setup(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryDto(CategoryId, "Primera")]);
        structureMock.Setup(s => s.GetCategoryAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureCategoryDto(CategoryId, "Primera"));
        structureMock.Setup(s => s.GetCategoryAthletesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryAthleteDto(AthleteId, true)]);

        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 34.0m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId, 10, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, 10)], []));
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(AthleteId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var aiMock = new Mock<IAiService>();
        aiMock.Setup(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingRecommendationRequestDto req, CancellationToken _) =>
                RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(req, "Reglas"));

        var controller = new RecommendationsController(
            new GenerateAthleteRecommendationsCommandHandler(structureMock.Object, HabitsRecommendationsClientFactory.Enabled(), seguimientoMock.Object, repository, aiMock.Object, uow),
            new GenerateCategoryRecommendationsCommandHandler(structureMock.Object, HabitsRecommendationsClientFactory.Enabled(), seguimientoMock.Object, repository, aiMock.Object, uow),
            new GetRecommendationsQueryHandler(repository),
            new GetRecommendationByIdQueryHandler(repository),
            new UpdateRecommendationCommandHandler(repository, uow),
            new ApproveRecommendationCommandHandler(repository, uow),
            new DiscardRecommendationCommandHandler(repository, uow));

        // Primera ejecución genera 1 -> 201 Created
        var firstAction = await controller.GenerateForAthlete(AthleteId);
        var created = Assert.IsType<CreatedResult>(firstAction.Result);
        var firstBody = Assert.IsType<RunAthleteRecommendationEngineResponseDto>(created.Value);
        Assert.Equal(1, firstBody.GeneratedCount);
        Assert.Equal($"/api/v1/recommendations?athleteId={AthleteId}", created.Location);

        // Ejecución por categoría con la misma recomendación ya pendiente -> 200 OK sin duplicar
        var catAction = await controller.GenerateForCategory(CategoryId);
        var catOk = Assert.IsType<OkObjectResult>(catAction.Result);
        var catBody = Assert.IsType<RunCategoryRecommendationEngineResponseDto>(catOk.Value);
        Assert.Equal(0, catBody.GeneratedCount);
        Assert.Equal(1, catBody.ExistingPendingCount);

        // GET list & GET by id
        var listAction = await controller.List(AthleteId, CategoryId, "Pending");
        var listOk = Assert.IsType<OkObjectResult>(listAction.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<AthleteRecommendationResponseDto>>(listOk.Value);
        var single = Assert.Single(items);

        var detailAction = await controller.GetById(single.Id);
        var detailOk = Assert.IsType<OkObjectResult>(detailAction.Result);
        var detail = Assert.IsType<AthleteRecommendationResponseDto>(detailOk.Value);
        Assert.Equal("salto por debajo del promedio", detail.Reason);
    }

    [Fact]
    public void Analyze_GivenDefaultTenPercentThreshold_IgnoresFivePercentGapAndRespectsConfiguredThreshold()
    {
        var athleteTests = new List<AthleteFitnessTestRemoteDto>
        {
            // 38 vs 40 = -5.0 % (dentro del margen de -10 % por defecto)
            new(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 38.0m, "cm", 1, new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc))
        };

        var averages = new CategoryAveragesRemoteDto(
            CategoryId,
            10,
            new DateOnly(2026, 11, 9),
            new DateOnly(2026, 11, 15),
            [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 40.0, 10)],
            []);

        var defaultResult = AthleteProfileAnalyzer.Analyze(athleteTests, averages, weeklyTrends: null);
        Assert.Empty(defaultResult.Signals);
        var defaultMetric = Assert.Single(defaultResult.EvaluatedMetrics);
        Assert.Equal(-5.0, defaultMetric.PercentageDifference);
        Assert.Contains("En el promedio", defaultMetric.Trend);

        // Con umbral configurado en -4 % (o 4.0 positivo normalizado a -4.0), -5 % sí dispara señal
        var strictResult = AthleteProfileAnalyzer.Analyze(athleteTests, averages, weeklyTrends: null, belowAverageThresholdPercent: -4.0);
        var signal = Assert.Single(strictResult.Signals);
        Assert.Equal("salto por debajo del promedio", signal.Reason);
        Assert.Contains("Por debajo del promedio", strictResult.EvaluatedMetrics[0].Trend);
    }

    [Fact]
    public async Task GenerateForAthlete_GivenConfiguredOptions_AppliesConfiguredThreshold()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        structureMock.SetupHabitsPermission(AthleteId);
        structureMock.Setup(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryDto(CategoryId, "Primera División")]);

        // 37.2 vs 40 = -7.0 % (no supera -10 % por defecto, pero sí -5 % configurado)
        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 37.2m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId, 10, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 40.0, 10)], []));
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(AthleteId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var aiMock = new Mock<IAiService>();
        aiMock.Setup(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingRecommendationRequestDto req, CancellationToken _) =>
                RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(req, "Reglas"));

        var defaultHandler = new GenerateAthleteRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow);

        var defaultResponse = await defaultHandler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));
        Assert.Equal(0, defaultResponse.GeneratedCount);
        Assert.Empty(store);

        var customHandler = new GenerateAthleteRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow,
            new RecommendationsOptions { BelowAverageThresholdPercent = -5.0 });

        var customResponse = await customHandler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));
        Assert.Equal(1, customResponse.GeneratedCount);
        Assert.Single(store);
    }

    [Fact]
    public async Task GenerateForAthlete_WhenRecommendationWasDiscarded_DoesNotRecreateOrCallAi()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var discarded = AthleteRecommendation.Create(
            AthleteId,
            "Refuerzo de salto",
            "salto por debajo del promedio",
            "Texto previo descartado por el PF.",
            categoryId: CategoryId,
            categoryName: "Primera División");
        discarded.Discard();
        await repository.AddAsync(discarded);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        structureMock.SetupHabitsPermission(AthleteId);
        structureMock.Setup(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryDto(CategoryId, "Primera División")]);

        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 34.0m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId, 10, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, 10)], []));
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(AthleteId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var aiMock = new Mock<IAiService>();

        var handler = new GenerateAthleteRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow);

        var response = await handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));

        Assert.Equal(0, response.GeneratedCount);
        Assert.Equal(0, response.ExistingPendingCount);
        Assert.Empty(response.Recommendations);
        Assert.Single(store);
        Assert.Equal(RecommendationStatus.Discarded, store[0].Status);
        Assert.Equal(0, uow.SaveChangesCount);
        aiMock.Verify(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateForCategory_WhenRecommendationWasDiscarded_DoesNotRecreateOrCallAi()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var discarded = AthleteRecommendation.Create(
            AthleteId,
            "Refuerzo de salto",
            "salto por debajo del promedio",
            "Texto previo descartado por el PF.",
            categoryId: CategoryId,
            categoryName: "Reserva");
        discarded.Discard();
        await repository.AddAsync(discarded);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.GetCategoryAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureCategoryDto(CategoryId, "Reserva"));
        structureMock.Setup(s => s.GetCategoryAthletesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StructureCategoryAthleteDto(AthleteId, true),
                new StructureCategoryAthleteDto(SecondAthleteId, true)
            ]);
        structureMock.SetupHabitsPermission(AthleteId);
        structureMock.SetupHabitsPermission(SecondAthleteId);

        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId, 8, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, 8)], []));

        // Ambos atletas están -19 % en salto, pero el Atleta 1 ya tiene esa recomendación descartada
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 34.0m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(SecondAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), SecondAthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 34.0m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(It.IsAny<Guid>(), 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var aiMock = new Mock<IAiService>();
        aiMock.Setup(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingRecommendationRequestDto req, CancellationToken _) =>
                RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(req, "Reglas"));

        var handler = new GenerateCategoryRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow);

        var response = await handler.HandleAsync(new GenerateCategoryRecommendationsCommand(CategoryId));

        Assert.Equal(2, response.EvaluatedAthletes);
        Assert.Equal(1, response.GeneratedCount);
        Assert.Equal(0, response.ExistingPendingCount);
        var returned = Assert.Single(response.Recommendations);
        Assert.Equal(SecondAthleteId, returned.AthleteId);
        Assert.Equal(2, store.Count);
        aiMock.Verify(a => a.GenerateTrainingRecommendationAsync(It.Is<TrainingRecommendationRequestDto>(r => r.AthleteId == AthleteId), It.IsAny<CancellationToken>()), Times.Never);
        aiMock.Verify(a => a.GenerateTrainingRecommendationAsync(It.Is<TrainingRecommendationRequestDto>(r => r.AthleteId == SecondAthleteId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RecommendationsOptions_GivenConfigurationAndEdgeValues_BindsAndNormalizesThreshold()
    {
        var configNegative = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RecommendationsOptions.SectionName}:BelowAverageThresholdPercent"] = "-15"
            })
            .Build();
        var boundNegative = configNegative.GetSection(RecommendationsOptions.SectionName).Get<RecommendationsOptions>()!;
        Assert.Equal(-15.0, boundNegative.GetEffectiveBelowAverageThresholdPercent());

        var configPositive = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RecommendationsOptions.SectionName}:BelowAverageThresholdPercent"] = "12.5"
            })
            .Build();
        var boundPositive = configPositive.GetSection(RecommendationsOptions.SectionName).Get<RecommendationsOptions>()!;
        Assert.Equal(-12.5, boundPositive.GetEffectiveBelowAverageThresholdPercent());

        Assert.Equal(-10.0, new RecommendationsOptions { BelowAverageThresholdPercent = 0 }.GetEffectiveBelowAverageThresholdPercent());
        Assert.Equal(-10.0, new RecommendationsOptions { BelowAverageThresholdPercent = double.NaN }.GetEffectiveBelowAverageThresholdPercent());
        Assert.Equal(-10.0, new RecommendationsOptions { BelowAverageThresholdPercent = double.PositiveInfinity }.GetEffectiveBelowAverageThresholdPercent());
    }

    [Fact]
    public async Task GenerateForCategory_GivenConfiguredOptions_AppliesConfiguredThreshold()
    {
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.GetCategoryAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureCategoryDto(CategoryId, "Reserva"));
        structureMock.Setup(s => s.GetCategoryAthletesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryAthleteDto(AthleteId, true)]);
        structureMock.SetupHabitsPermission(AthleteId);

        // 37.2 vs 40 = -7.0 %: no dispara con -10 % default, sí con -5 % configurado
        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId, 8, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 40.0, 8)], []));
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AthleteFitnessTestRemoteDto(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 37.2m, "cm", 1, DateTime.UtcNow)
            ]);
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(AthleteId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var aiMock = new Mock<IAiService>();
        aiMock.Setup(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingRecommendationRequestDto req, CancellationToken _) =>
                RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(req, "Reglas"));

        var defaultHandler = new GenerateCategoryRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow);

        var defaultResponse = await defaultHandler.HandleAsync(new GenerateCategoryRecommendationsCommand(CategoryId));
        Assert.Equal(0, defaultResponse.GeneratedCount);
        Assert.Empty(store);

        var customHandler = new GenerateCategoryRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow,
            new RecommendationsOptions { BelowAverageThresholdPercent = -5.0 });

        var customResponse = await customHandler.HandleAsync(new GenerateCategoryRecommendationsCommand(CategoryId));
        Assert.Equal(1, customResponse.GeneratedCount);
        Assert.Single(store);
    }

    [Fact]
    public void Analyze_GivenPlankInMinutesOrSeconds_TreatsHigherTimeAsBetter_WhileTimedRunsStayLowerIsBetter()
    {
        Assert.False(AthleteProfileAnalyzer.IsLowerBetter("min", "Plancha frontal"));
        Assert.False(AthleteProfileAnalyzer.IsLowerBetter("segundos", "Plancha isométrica"));
        Assert.False(AthleteProfileAnalyzer.IsLowerBetter("min", "Sentadilla isométrica"));
        Assert.True(AthleteProfileAnalyzer.IsLowerBetter("s", "Carrera 30 m"));
        Assert.True(AthleteProfileAnalyzer.IsLowerBetter("min", "Test de Bronco"));
        Assert.True(AthleteProfileAnalyzer.IsLowerBetter("min", "Carrera 1 km"));

        // Atleta aguanta 3.0 min de plancha frente a 2.0 min de promedio -> +50 % (superior al promedio, sin señal)
        var strongPlank = AthleteProfileAnalyzer.Analyze(
            [new(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Plancha frontal", "Core", 3.0m, "min", 1, DateTime.UtcNow)],
            new CategoryAveragesRemoteDto(
                CategoryId, 10, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Plancha frontal", "min", 2.0, 10)], []),
            weeklyTrends: null);

        Assert.Empty(strongPlank.Signals);
        var strongMetric = Assert.Single(strongPlank.EvaluatedMetrics);
        Assert.Equal(50.0, strongMetric.PercentageDifference);
        Assert.Contains("Superior al promedio", strongMetric.Trend);

        // Atleta aguanta 1.2 min de plancha frente a 2.0 min de promedio -> -40 % (por debajo, dispara señal)
        var weakPlank = AthleteProfileAnalyzer.Analyze(
            [new(Guid.NewGuid(), AthleteId, Guid.NewGuid(), "Plancha frontal", "Core", 1.2m, "min", 1, DateTime.UtcNow)],
            new CategoryAveragesRemoteDto(
                CategoryId, 10, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Plancha frontal", "min", 2.0, 10)], []),
            weeklyTrends: null);

        var weakSignal = Assert.Single(weakPlank.Signals);
        Assert.Equal(-40.0, weakSignal.Metrics[0].PercentageDifference);
        Assert.Contains("Por debajo del promedio", weakSignal.Metrics[0].Trend);
    }

    [Fact]
    public void DeserializeSnapshot_GivenMalformedJson_LogsWarningAndReturnsEmpty()
    {
        var loggerMock = new Mock<ILogger>();

        var (metrics, trends) = RecommendationMapper.DeserializeSnapshot("{ json roto", loggerMock.Object);

        Assert.Empty(metrics);
        Assert.Null(trends);
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<System.Text.Json.JsonException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateForCategory_GivenMultipleAthletes_ParallelizesWithinConfiguredLimitAndSavesOnce()
    {
        var athleteIds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList();
        var store = new List<AthleteRecommendation>();
        var repository = new InMemoryTenantRecommendationRepository(store, () => PfId);
        var uow = new FakeUnitOfWork();

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.GetCategoryAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureCategoryDto(CategoryId, "Primera"));
        structureMock.Setup(s => s.GetCategoryAthletesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athleteIds.Select(id => new StructureCategoryAthleteDto(id, true)).ToList());
        foreach (var id in athleteIds)
            structureMock.SetupHabitsPermission(id);

        var seguimientoMock = new Mock<ISeguimientoClient>();
        seguimientoMock.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId, athleteIds.Count, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15),
                [new FitnessTestAverageRemoteDto("Salto Vertical CMJ", "cm", 42.0, athleteIds.Count)], []));
        seguimientoMock.Setup(s => s.GetAthleteFitnessTestsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                [new AthleteFitnessTestRemoteDto(Guid.NewGuid(), id, Guid.NewGuid(), "Salto Vertical CMJ", "Power", 34.0m, "cm", 1, DateTime.UtcNow)]);
        seguimientoMock.Setup(s => s.GetAthleteWeeklyTrendsAsync(It.IsAny<Guid>(), 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var inFlight = 0;
        var peakConcurrency = 0;
        var aiMock = new Mock<IAiService>();
        aiMock.Setup(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (TrainingRecommendationRequestDto req, CancellationToken ct) =>
            {
                var current = Interlocked.Increment(ref inFlight);
                while (true)
                {
                    var observed = Volatile.Read(ref peakConcurrency);
                    if (current <= observed || Interlocked.CompareExchange(ref peakConcurrency, current, observed) == observed)
                        break;
                }

                await Task.Delay(40, ct);
                Interlocked.Decrement(ref inFlight);
                return RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(req, "Reglas");
            });

        const int maxParallelism = 3;
        var handler = new GenerateCategoryRecommendationsCommandHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uow,
            new RecommendationsOptions { MaxCategoryParallelism = maxParallelism });

        var response = await handler.HandleAsync(new GenerateCategoryRecommendationsCommand(CategoryId));

        Assert.Equal(athleteIds.Count, response.EvaluatedAthletes);
        Assert.Equal(athleteIds.Count, response.GeneratedCount);
        Assert.Equal(athleteIds.Count, store.Count);
        Assert.Equal(1, uow.SaveChangesCount);
        Assert.InRange(peakConcurrency, 2, maxParallelism);
    }
}
