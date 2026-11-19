using API.Controllers;
using Application.Common.Helpers;
using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.GenerateAthleteRecommendations;
using Application.UseCases.Recommendations.GenerateCategoryRecommendations;
using Application.UseCases.Recommendations.GetRecommendationById;
using Application.UseCases.Recommendations.GetRecommendations;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
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

        public Task<IReadOnlyList<AthleteRecommendation>> ListPendingByAthleteIdsAsync(
            IReadOnlyCollection<Guid> athleteIds,
            CancellationToken cancellationToken = default)
        {
            if (athleteIds.Count == 0)
                return Task.FromResult<IReadOnlyList<AthleteRecommendation>>([]);

            var pfId = currentPfId();
            var set = athleteIds.ToHashSet();
            IReadOnlyList<AthleteRecommendation> result = store
                .Where(r => r.PfId == pfId &&
                            r.DeletedAt == null &&
                            set.Contains(r.AthleteId) &&
                            r.Status == RecommendationStatus.Pending)
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

        var handlerStructureDown = new GenerateAthleteRecommendationsCommandHandler(
            structureDown.Object,
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

        var seguimientoDown = new Mock<ISeguimientoClient>();
        seguimientoDown.Setup(s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SeguimientoServiceUnavailableException("Seguimiento no disponible."));
        seguimientoDown.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CategoryAveragesRemoteDto?)null);
        seguimientoDown.Setup(s => s.GetAthleteWeeklyTrendsAsync(AthleteId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var handlerSeguimientoDown = new GenerateAthleteRecommendationsCommandHandler(
            structureOk.Object,
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
            new GenerateAthleteRecommendationsCommandHandler(structureMock.Object, seguimientoMock.Object, repository, aiMock.Object, uow),
            new GenerateCategoryRecommendationsCommandHandler(structureMock.Object, seguimientoMock.Object, repository, aiMock.Object, uow),
            new GetRecommendationsQueryHandler(repository),
            new GetRecommendationByIdQueryHandler(repository));

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
}
