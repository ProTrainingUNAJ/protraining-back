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
using Moq;
using Xunit;

namespace Asistente.Tests.Security;

// KAN-491 + KAN-432: un PF no ve ni genera recomendaciones sobre atletas o categorías de otro PF (404).
[Trait("Category", "Isolation")]
public sealed class RecommendationIsolationTests
{
    private static readonly Guid PfA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid PfB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid AthleteOfPfA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CategoryOfPfA = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static void SetPfId(BaseEntity entity, Guid pfId) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(entity, pfId);

    private sealed class TenantScopedInMemoryRepository(
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
            return Task.FromResult(store.FirstOrDefault(r => r.Id == id && r.PfId == pfId && r.DeletedAt == null));
        }

        public Task<IReadOnlyList<AthleteRecommendation>> ListAsync(
            Guid? athleteId = null,
            Guid? categoryId = null,
            RecommendationStatus? status = null,
            CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            IReadOnlyList<AthleteRecommendation> items = store
                .Where(r => r.PfId == pfId && r.DeletedAt == null)
                .Where(r => !athleteId.HasValue || r.AthleteId == athleteId.Value)
                .Where(r => !categoryId.HasValue || r.CategoryId == categoryId.Value)
                .Where(r => !status.HasValue || r.Status == status.Value)
                .ToList();
            return Task.FromResult(items);
        }

        public Task<IReadOnlyList<AthleteRecommendation>> ListPendingByAthleteIdsAsync(
            IReadOnlyCollection<Guid> athleteIds,
            CancellationToken cancellationToken = default)
        {
            var pfId = currentPfId();
            var set = athleteIds.ToHashSet();
            IReadOnlyList<AthleteRecommendation> items = store
                .Where(r => r.PfId == pfId && r.DeletedAt == null && set.Contains(r.AthleteId) && r.Status == RecommendationStatus.Pending)
                .ToList();
            return Task.FromResult(items);
        }
    }

    [Fact]
    public async Task Given_RecommendationOwnedByPfA_When_PfBListsOrGetsById_Then_CannotSeeAndReceives404()
    {
        var activePf = PfA;
        var store = new List<AthleteRecommendation>();
        var repository = new TenantScopedInMemoryRepository(store, () => activePf);

        var recOfPfA = AthleteRecommendation.Create(
            AthleteOfPfA,
            "Ajuste de salto",
            "salto por debajo del promedio",
            "Texto de prueba",
            CategoryOfPfA,
            "Primera");
        await repository.AddAsync(recOfPfA);

        activePf = PfB;
        var listHandler = new GetRecommendationsQueryHandler(repository);
        var getByIdHandler = new GetRecommendationByIdQueryHandler(repository);

        var visibleToPfB = await listHandler.HandleAsync(new GetRecommendationsQuery());
        Assert.Empty(visibleToPfB);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            getByIdHandler.HandleAsync(new GetRecommendationByIdQuery(recOfPfA.Id)));
    }

    [Fact]
    public async Task Given_AthleteOrCategoryOwnedByPfA_When_PfBRunsEngine_Then_ThrowsNotFound404WithoutCallingAi()
    {
        var activePf = PfB;
        var store = new List<AthleteRecommendation>();
        var repository = new TenantScopedInMemoryRepository(store, () => activePf);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteOfPfA, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        structureMock.Setup(s => s.GetCategoryAsync(CategoryOfPfA, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StructureCategoryDto?)null);

        var seguimientoMock = new Mock<ISeguimientoClient>();
        var aiMock = new Mock<IAiService>();
        var uowMock = new Mock<IUnitOfWork>();

        var athleteHandler = new GenerateAthleteRecommendationsCommandHandler(
            structureMock.Object,
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uowMock.Object);

        var categoryHandler = new GenerateCategoryRecommendationsCommandHandler(
            structureMock.Object,
            seguimientoMock.Object,
            repository,
            aiMock.Object,
            uowMock.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            athleteHandler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteOfPfA)));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            categoryHandler.HandleAsync(new GenerateCategoryRecommendationsCommand(CategoryOfPfA)));

        aiMock.Verify(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
