using System.Reflection;
using API.Controllers;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.ApproveRecommendation;
using Application.UseCases.Recommendations.DiscardRecommendation;
using Application.UseCases.Recommendations.GenerateAthleteRecommendations;
using Application.UseCases.Recommendations.GenerateCategoryRecommendations;
using Application.UseCases.Recommendations.GetMyRecommendations;
using Application.UseCases.Recommendations.GetRecommendationById;
using Application.UseCases.Recommendations.GetRecommendations;
using Application.UseCases.Recommendations.UpdateRecommendation;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y de flujo de revisión/aprobación de recomendaciones para el PF y entrega al atleta (#197 · KAN-492).
/// </summary>
public sealed class RecommendationReviewTests
{
    private static readonly Guid PfId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AthleteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CategoryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static void SetPfId(BaseEntity entity, Guid pfId) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(entity, pfId);

    private sealed class InMemoryRecommendationStore : IAthleteRecommendationRepository, IUnitOfWork
    {
        public List<AthleteRecommendation> Items { get; } = [];
        public int SaveChangesCount { get; private set; }

        public Task AddAsync(AthleteRecommendation recommendation, CancellationToken cancellationToken = default)
        {
            SetPfId(recommendation, PfId);
            Items.Add(recommendation);
            return Task.CompletedTask;
        }

        public Task<AthleteRecommendation?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(r => r.Id == id && r.DeletedAt == null));

        public Task<AthleteRecommendation?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(r => r.Id == id && r.DeletedAt == null));

        public Task<IReadOnlyList<AthleteRecommendation>> ListAsync(
            Guid? athleteId = null,
            Guid? categoryId = null,
            RecommendationStatus? status = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AthleteRecommendation> result = Items
                .Where(r => r.DeletedAt == null)
                .Where(r => !athleteId.HasValue || r.AthleteId == athleteId.Value)
                .Where(r => !categoryId.HasValue || r.CategoryId == categoryId.Value)
                .Where(r => !status.HasValue || r.Status == status.Value)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<AthleteRecommendation>> ListApprovedByAthleteAsync(
            Guid athleteId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AthleteRecommendation> result = Items
                .Where(r => r.DeletedAt == null && r.AthleteId == athleteId && r.Status == RecommendationStatus.Approved)
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

            var athleteSet = athleteIds.ToHashSet();
            var statusSet = statuses.ToHashSet();
            IReadOnlyList<AthleteRecommendation> result = Items
                .Where(r => r.DeletedAt == null && athleteSet.Contains(r.AthleteId) && statusSet.Contains(r.Status))
                .ToList();
            return Task.FromResult(result);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }

    [Fact]
    public async Task AcceptanceCriterion_Given_PendingRecommendation_When_PfApproves_Then_AthleteSeesItInApp()
    {
        var store = new InMemoryRecommendationStore();
        var pending = AthleteRecommendation.Create(
            AthleteId,
            "Mejora de salto vertical",
            "salto por debajo del promedio",
            "Sumar dos bloques de saltos pliométricos controlados por semana.",
            CategoryId,
            "Primera División");
        await store.AddAsync(pending);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.GetMyAthleteIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AthleteId);
        structureMock.SetupMyHabitsPermission();

        var myQueryHandler = new GetMyRecommendationsQueryHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            store);
        var approveHandler = new ApproveRecommendationCommandHandler(store, store);

        // Antes de aprobar: el atleta NO ve la recomendación pendiente
        var beforeApproval = await myQueryHandler.HandleAsync(new GetMyRecommendationsQuery());
        Assert.Empty(beforeApproval);

        // El PF aprueba la recomendación
        var approvedDto = await approveHandler.HandleAsync(new ApproveRecommendationCommand(pending.Id));
        Assert.Equal("Approved", approvedDto.Status);
        Assert.NotNull(approvedDto.ReviewedAtUtc);
        Assert.Equal(1, store.SaveChangesCount);

        // Después de aprobar: el atleta la ve inmediatamente en su app
        var afterApproval = await myQueryHandler.HandleAsync(new GetMyRecommendationsQuery());
        var visible = Assert.Single(afterApproval);
        Assert.Equal(pending.Id, visible.Id);
        Assert.Equal("Mejora de salto vertical", visible.Title);
        Assert.Equal("salto por debajo del promedio", visible.Reason);
        Assert.Equal("Sumar dos bloques de saltos pliométricos controlados por semana.", visible.RecommendationText);
        Assert.Equal("Primera División", visible.CategoryName);
        Assert.NotNull(visible.ReviewedAtUtc);
    }

    [Fact]
    public async Task Given_PendingRecommendation_When_PfEditsContent_Then_UpdatesTitleAndTextKeepingReasonImmutable()
    {
        var store = new InMemoryRecommendationStore();
        var pending = AthleteRecommendation.Create(
            AthleteId,
            "Título original",
            "salto por debajo del promedio",
            "Texto generado por IA",
            CategoryId,
            "Primera");
        await store.AddAsync(pending);

        var handler = new UpdateRecommendationCommandHandler(store, store);
        var updated = await handler.HandleAsync(new UpdateRecommendationCommand(
            pending.Id,
            "  Foco en potencia de piernas  ",
            "  Agregamos 3x5 saltos al cajón al inicio del martes y jueves.  "));

        Assert.Equal("Pending", updated.Status);
        Assert.Equal("Foco en potencia de piernas", updated.Title);
        Assert.Equal("Agregamos 3x5 saltos al cajón al inicio del martes y jueves.", updated.RecommendationText);
        Assert.Equal("salto por debajo del promedio", updated.Reason);
        Assert.Equal(1, store.SaveChangesCount);
    }

    [Fact]
    public async Task Given_PendingRecommendation_When_PfApprovesWithEdits_Then_SavesNewTextAndPublishesToAthlete()
    {
        var store = new InMemoryRecommendationStore();
        var pending = AthleteRecommendation.Create(
            AthleteId,
            "Título original",
            "salto por debajo del promedio",
            "Texto original",
            CategoryId,
            "Primera");
        await store.AddAsync(pending);

        var approveHandler = new ApproveRecommendationCommandHandler(store, store);
        var result = await approveHandler.HandleAsync(new ApproveRecommendationCommand(
            pending.Id,
            "Título editado por el PF",
            "Texto personalizado por el PF antes de enviar."));

        Assert.Equal("Approved", result.Status);
        Assert.Equal("Título editado por el PF", result.Title);
        Assert.Equal("Texto personalizado por el PF antes de enviar.", result.RecommendationText);
        Assert.NotNull(result.ReviewedAtUtc);
        Assert.Equal(1, store.SaveChangesCount);
    }

    [Fact]
    public async Task Given_PendingRecommendation_When_PfDiscards_Then_MarksDiscardedAndNeverReachesAthlete()
    {
        var store = new InMemoryRecommendationStore();
        var pending = AthleteRecommendation.Create(
            AthleteId,
            "Ajuste descartable",
            "caída de bienestar",
            "Texto a descartar",
            CategoryId,
            "Primera");
        await store.AddAsync(pending);

        var discardHandler = new DiscardRecommendationCommandHandler(store, store);
        var discarded = await discardHandler.HandleAsync(new DiscardRecommendationCommand(pending.Id));

        Assert.Equal("Discarded", discarded.Status);
        Assert.NotNull(discarded.ReviewedAtUtc);
        Assert.Equal(1, store.SaveChangesCount);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.GetMyAthleteIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AthleteId);
        structureMock.SetupMyHabitsPermission();

        var myQueryHandler = new GetMyRecommendationsQueryHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            store);
        var visible = await myQueryHandler.HandleAsync(new GetMyRecommendationsQuery());
        Assert.Empty(visible);
    }

    [Fact]
    public async Task Given_AlreadyApprovedOrDiscardedRecommendation_When_PfEditsApprovesOrDiscards_Then_ThrowsConflict409()
    {
        var store = new InMemoryRecommendationStore();
        var approved = AthleteRecommendation.Create(AthleteId, "Aprobada", "salto por debajo del promedio", "Texto");
        approved.Approve();
        var discarded = AthleteRecommendation.Create(AthleteId, "Descartada", "fatiga", "Texto");
        discarded.Discard();
        await store.AddAsync(approved);
        await store.AddAsync(discarded);

        var updateHandler = new UpdateRecommendationCommandHandler(store, store);
        var approveHandler = new ApproveRecommendationCommandHandler(store, store);
        var discardHandler = new DiscardRecommendationCommandHandler(store, store);

        await Assert.ThrowsAsync<ConflictException>(() =>
            updateHandler.HandleAsync(new UpdateRecommendationCommand(approved.Id, "Nuevo", "Texto")));
        await Assert.ThrowsAsync<ConflictException>(() =>
            approveHandler.HandleAsync(new ApproveRecommendationCommand(approved.Id)));
        await Assert.ThrowsAsync<ConflictException>(() =>
            discardHandler.HandleAsync(new DiscardRecommendationCommand(approved.Id)));

        await Assert.ThrowsAsync<ConflictException>(() =>
            updateHandler.HandleAsync(new UpdateRecommendationCommand(discarded.Id, "Nuevo", "Texto")));
        await Assert.ThrowsAsync<ConflictException>(() =>
            approveHandler.HandleAsync(new ApproveRecommendationCommand(discarded.Id)));
        await Assert.ThrowsAsync<ConflictException>(() =>
            discardHandler.HandleAsync(new DiscardRecommendationCommand(discarded.Id)));

        Assert.Equal(0, store.SaveChangesCount);
    }

    [Fact]
    public async Task Given_InvalidInputOrMissingId_When_PfReviews_Then_ThrowsDomain400OrNotFound404()
    {
        var store = new InMemoryRecommendationStore();
        var pending = AthleteRecommendation.Create(AthleteId, "Título", "motivo", "Texto");
        await store.AddAsync(pending);

        var updateHandler = new UpdateRecommendationCommandHandler(store, store);
        var approveHandler = new ApproveRecommendationCommandHandler(store, store);
        var discardHandler = new DiscardRecommendationCommandHandler(store, store);

        await Assert.ThrowsAsync<DomainException>(() =>
            updateHandler.HandleAsync(new UpdateRecommendationCommand(pending.Id, "   ", "Texto válido")));
        await Assert.ThrowsAsync<DomainException>(() =>
            updateHandler.HandleAsync(new UpdateRecommendationCommand(pending.Id, "Título válido", "")));
        await Assert.ThrowsAsync<DomainException>(() =>
            approveHandler.HandleAsync(new ApproveRecommendationCommand(pending.Id, "   ", null)));

        var missingId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            updateHandler.HandleAsync(new UpdateRecommendationCommand(missingId, "T", "Texto")));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            approveHandler.HandleAsync(new ApproveRecommendationCommand(missingId)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            discardHandler.HandleAsync(new DiscardRecommendationCommand(missingId)));

        Assert.Equal(0, store.SaveChangesCount);
    }

    [Fact]
    public async Task Given_UnlinkedAthleteOrStructureDown_When_GettingMyRecommendations_Then_ReturnsEmptyOrThrows503()
    {
        var store = new InMemoryRecommendationStore();
        var unlinkedMock = new Mock<IStructureAthleteClient>();
        unlinkedMock.Setup(s => s.GetMyAthleteIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var handlerUnlinked = new GetMyRecommendationsQueryHandler(
            unlinkedMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            store);
        var emptyResult = await handlerUnlinked.HandleAsync(new GetMyRecommendationsQuery());
        Assert.Empty(emptyResult);

        var downMock = new Mock<IStructureAthleteClient>();
        downMock.Setup(s => s.GetMyAthleteIdAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureServiceUnavailableException("Estructura caído"));

        var handlerDown = new GetMyRecommendationsQueryHandler(
            downMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            store);
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            handlerDown.HandleAsync(new GetMyRecommendationsQuery()));
    }

    [Fact]
    public async Task Given_MultipleApprovedRecommendations_When_AthleteQueriesMine_Then_OrdersMostRecentlyApprovedFirst()
    {
        var store = new InMemoryRecommendationStore();
        var older = AthleteRecommendation.Create(AthleteId, "Primera aprobada", "motivo 1", "Texto 1");
        older.Approve(new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc));
        var newer = AthleteRecommendation.Create(AthleteId, "Segunda aprobada", "motivo 2", "Texto 2");
        newer.Approve(new DateTime(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc));
        await store.AddAsync(older);
        await store.AddAsync(newer);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.GetMyAthleteIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AthleteId);
        structureMock.SetupMyHabitsPermission();

        var controller = new MeRecommendationsController(new GetMyRecommendationsQueryHandler(
            structureMock.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            store));
        var action = await controller.ListMine();
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<MyRecommendationResponseDto>>(ok.Value);

        Assert.Equal(2, items.Count);
        Assert.Equal(newer.Id, items[0].Id);
        Assert.Equal(older.Id, items[1].Id);
    }

    [Fact]
    public async Task Controllers_EnforceExpectedRolesAndExecuteUpdateApproveDiscard()
    {
        var pfAttr = typeof(RecommendationsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(pfAttr);
        Assert.Equal("PreparadorFisico", pfAttr!.Roles);

        var athleteAttr = typeof(MeRecommendationsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(athleteAttr);
        Assert.Equal("Atleta", athleteAttr!.Roles);

        var store = new InMemoryRecommendationStore();
        var rec1 = AthleteRecommendation.Create(AthleteId, "Rec 1", "salto por debajo del promedio", "Texto 1");
        var rec2 = AthleteRecommendation.Create(AthleteId, "Rec 2", "carga alta", "Texto 2");
        await store.AddAsync(rec1);
        await store.AddAsync(rec2);

        var structureMock = new Mock<IStructureAthleteClient>();
        var seguimientoMock = new Mock<ISeguimientoClient>();
        var aiMock = new Mock<IAiService>();

        var controller = new RecommendationsController(
            new GenerateAthleteRecommendationsCommandHandler(structureMock.Object, HabitsRecommendationsClientFactory.Enabled(), seguimientoMock.Object, store, aiMock.Object, store),
            new GenerateCategoryRecommendationsCommandHandler(structureMock.Object, HabitsRecommendationsClientFactory.Enabled(), seguimientoMock.Object, store, ScopedAiServiceRunnerFactory.From(aiMock.Object), store),
            new GetRecommendationsQueryHandler(store),
            new GetRecommendationByIdQueryHandler(store),
            new UpdateRecommendationCommandHandler(store, store),
            new ApproveRecommendationCommandHandler(store, store),
            new DiscardRecommendationCommandHandler(store, store));

        var updateRes = await controller.Update(rec1.Id, new UpdateRecommendationRequestDto("Rec 1 editada", "Nuevo texto 1"));
        var updateOk = Assert.IsType<OkObjectResult>(updateRes.Result);
        Assert.Equal("Rec 1 editada", Assert.IsType<AthleteRecommendationResponseDto>(updateOk.Value).Title);

        var approveRes = await controller.Approve(rec1.Id);
        var approveOk = Assert.IsType<OkObjectResult>(approveRes.Result);
        Assert.Equal("Approved", Assert.IsType<AthleteRecommendationResponseDto>(approveOk.Value).Status);

        var discardRes = await controller.Discard(rec2.Id);
        var discardOk = Assert.IsType<OkObjectResult>(discardRes.Result);
        Assert.Equal("Discarded", Assert.IsType<AthleteRecommendationResponseDto>(discardOk.Value).Status);
    }
}
