using System.Net;
using System.Text;
using API.Controllers;
using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.GenerateAthleteRecommendations;
using Application.UseCases.Recommendations.GenerateCategoryRecommendations;
using Application.UseCases.Recommendations.GetMyRecommendations;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// KAN-493: la preferencia del PF sobre recomendaciones de hábitos y alimentación se respeta
/// en generación, nutrición, lectura del atleta y acceso directo al endpoint (fail-closed).
/// </summary>
public sealed class Kan493HabitsRecommendationsTests
{
    private static readonly Guid AthleteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAthleteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CategoryId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    #region Regla combinada de permisos

    [Fact]
    public void Policy_PfEnabled_AndAdult_AllowsEvenWithoutTutorFlag()
    {
        var permission = new StructureAthleteHabitsPermissionDto(
            IsMinor: false,
            AllowsHabitsRecommendations: false);

        Assert.True(HabitsRecommendationsPolicy.IsEnabled(true, permission));
    }

    [Fact]
    public void Policy_PfEnabled_AndMinorWithTutorPermission_Allows()
    {
        var permission = new StructureAthleteHabitsPermissionDto(
            IsMinor: true,
            AllowsHabitsRecommendations: true);

        Assert.True(HabitsRecommendationsPolicy.IsEnabled(true, permission));
    }

    [Fact]
    public void Policy_PfEnabled_AndMinorWithoutTutorPermission_Blocks()
    {
        var permission = new StructureAthleteHabitsPermissionDto(
            IsMinor: true,
            AllowsHabitsRecommendations: false);

        Assert.False(HabitsRecommendationsPolicy.IsEnabled(true, permission));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Policy_WhenPfPreferenceIsNotTrue_BlocksRegardlessOfTutor(bool? pfEnabled)
    {
        var permission = new StructureAthleteHabitsPermissionDto(
            IsMinor: true,
            AllowsHabitsRecommendations: true);

        Assert.False(HabitsRecommendationsPolicy.IsEnabled(pfEnabled, permission));
    }

    [Fact]
    public void Policy_WhenPermissionCouldNotBeQueried_Blocks()
    {
        Assert.False(HabitsRecommendationsPolicy.IsEnabled(true, null));
    }

    #endregion

    #region Generación de recomendaciones

    [Fact]
    public async Task GenerateForAthlete_WhenPfDisabled_DoesNotCallAiOrPersist()
    {
        var (structure, seguimiento, ai, repository, uow) = BuildGenerationDoubles();
        var identity = HabitsRecommendationsClientFactory.Create(enabled: false);
        var handler = BuildGenerationHandler(structure, identity, seguimiento, repository, ai, uow);

        var result = await handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));

        Assert.Empty(result.Recommendations);
        Assert.Equal(0, result.GeneratedCount);
        repository.Verify(r => r.AddAsync(It.IsAny<AthleteRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        ai.Verify(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        structure.Verify(s => s.GetHabitsPermissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateForAthlete_WhenMinorWithoutTutorPermission_DoesNotCallAiOrPersist()
    {
        var (structure, seguimiento, ai, repository, uow) = BuildGenerationDoubles();
        structure.SetupHabitsPermission(AthleteId, isMinor: true, allows: false);
        var identity = HabitsRecommendationsClientFactory.Enabled();
        var handler = BuildGenerationHandler(structure, identity, seguimiento, repository, ai, uow);

        var result = await handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));

        Assert.Empty(result.Recommendations);
        Assert.Equal(0, result.GeneratedCount);
        structure.Verify(s => s.GetHabitsPermissionAsync(AthleteId, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.AddAsync(It.IsAny<AthleteRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        ai.Verify(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateForAthlete_WhenPfEnabledAndMinorAuthorized_ContinuesPastPermissionCheck()
    {
        var (structure, seguimiento, ai, repository, uow) = BuildGenerationDoubles();
        structure.SetupHabitsPermission(AthleteId, isMinor: true, allows: true);
        var handler = BuildGenerationHandler(
            structure,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimiento,
            repository,
            ai,
            uow);

        await handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));

        structure.Verify(s => s.GetHabitsPermissionAsync(AthleteId, It.IsAny<CancellationToken>()), Times.Once);
        structure.Verify(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateForAthlete_WhenIdentityPreferenceCannotBeQueried_FailsClosed()
    {
        var (structure, seguimiento, ai, repository, uow) = BuildGenerationDoubles();
        var identity = HabitsRecommendationsClientFactory.Create(enabled: null);
        var handler = BuildGenerationHandler(structure, identity, seguimiento, repository, ai, uow);

        var result = await handler.HandleAsync(new GenerateAthleteRecommendationsCommand(AthleteId));

        Assert.Empty(result.Recommendations);
        structure.Verify(s => s.GetHabitsPermissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.AddAsync(It.IsAny<AthleteRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        ai.Verify(a => a.GenerateTrainingRecommendationAsync(It.IsAny<TrainingRecommendationRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateForCategory_WhenAthleteLacksTutorPermission_SkipsOnlyThatAthlete()
    {
        var structure = new Mock<IStructureAthleteClient>();
        structure.Setup(s => s.GetCategoryAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureCategoryDto(CategoryId, "Reserva"));
        structure.Setup(s => s.GetCategoryAthletesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StructureCategoryAthleteDto(AthleteId, true),
                new StructureCategoryAthleteDto(SecondAthleteId, true)
            ]);
        structure.SetupHabitsPermission(AthleteId, isMinor: true, allows: false);
        structure.SetupHabitsPermission(SecondAthleteId, isMinor: false, allows: false);

        var seguimiento = new Mock<ISeguimientoClient>();
        seguimiento.Setup(s => s.GetCategoryAveragesAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryAveragesRemoteDto(
                CategoryId,
                0,
                new DateOnly(2026, 11, 9),
                new DateOnly(2026, 11, 15),
                [],
                []));
        seguimiento
            .Setup(s => s.GetAthleteFitnessTestsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        seguimiento
            .Setup(s => s.GetAthleteWeeklyTrendsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var repository = new Mock<IAthleteRecommendationRepository>();
        repository
            .Setup(r => r.ListByAthleteIdsAndStatusesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<RecommendationStatus>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = new GenerateCategoryRecommendationsCommandHandler(
            structure.Object,
            HabitsRecommendationsClientFactory.Enabled(),
            seguimiento.Object,
            repository.Object,
            new Mock<IAiService>().Object,
            new Mock<IUnitOfWork>().Object);

        var result = await handler.HandleAsync(new GenerateCategoryRecommendationsCommand(CategoryId));

        Assert.Equal(1, result.EvaluatedAthletes);
        seguimiento.Verify(
            s => s.GetAthleteFitnessTestsAsync(AthleteId, It.IsAny<CancellationToken>()),
            Times.Never);
        seguimiento.Verify(
            s => s.GetAthleteFitnessTestsAsync(SecondAthleteId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Nutrición

    [Fact]
    public async Task Nutrition_WhenPfDisabled_DoesNotCallProvidersAndThrows()
    {
        var provider = new Mock<IAiClient>();
        var service = BuildNutritionService(provider.Object, HabitsRecommendationsClientFactory.Create(enabled: false));

        await Assert.ThrowsAsync<DomainException>(() =>
            service.GenerateNutritionRecommendationAsync(new NutritionRecommendationRequestDto(
                AthleticGoal: "Ganar masa muscular")));

        provider.Verify(
            p => p.TryGenerateNutritionRecommendationAsync(It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Nutrition_WhenPreferenceCannotBeQueried_FailsClosed()
    {
        var provider = new Mock<IAiClient>();
        var service = BuildNutritionService(provider.Object, HabitsRecommendationsClientFactory.Create(enabled: null));

        await Assert.ThrowsAsync<DomainException>(() =>
            service.GenerateNutritionRecommendationAsync(new NutritionRecommendationRequestDto(
                AthleticGoal: "Ganar masa muscular")));

        provider.Verify(
            p => p.TryGenerateNutritionRecommendationAsync(It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Acceso directo del atleta

    [Fact]
    public async Task GetMyRecommendations_ViaController_WhenPfDisabled_ReturnsEmptyEvenWithApprovedData()
    {
        var (controller, repository, structure) = BuildMeController(enabled: false);
        repository
            .Setup(r => r.ListApprovedByAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([BuildApprovedRecommendation()]);

        var action = await controller.ListMine();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<MyRecommendationResponseDto>>(ok.Value);
        Assert.Empty(items);
        repository.Verify(
            r => r.ListApprovedByAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        structure.Verify(s => s.GetMyHabitsPermissionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyRecommendations_ViaController_WhenMinorNotAuthorized_ReturnsEmpty()
    {
        var (controller, repository, structure) = BuildMeController(enabled: true);
        structure.SetupMyHabitsPermission(isMinor: true, allows: false);
        repository
            .Setup(r => r.ListApprovedByAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([BuildApprovedRecommendation()]);

        var action = await controller.ListMine();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<MyRecommendationResponseDto>>(ok.Value);
        Assert.Empty(items);
        repository.Verify(
            r => r.ListApprovedByAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetMyRecommendations_ViaController_WhenEnabledAndAuthorized_ReturnsApprovedRecommendation()
    {
        var (controller, repository, structure) = BuildMeController(enabled: true);
        structure.SetupMyHabitsPermission(isMinor: true, allows: true);
        var approved = BuildApprovedRecommendation();
        repository
            .Setup(r => r.ListApprovedByAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([approved]);

        var action = await controller.ListMine();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<MyRecommendationResponseDto>>(ok.Value);
        var visible = Assert.Single(items);
        Assert.Equal(approved.Id, visible.Id);
        repository.Verify(
            r => r.ListApprovedByAthleteAsync(AthleteId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetMyRecommendations_ViaController_WhenPreferenceQueryFails_FailsClosed()
    {
        var structure = new Mock<IStructureAthleteClient>();
        structure.Setup(s => s.GetMyAthleteIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AthleteId);
        structure.SetupMyHabitsPermission();
        var repository = new Mock<IAthleteRecommendationRepository>();
        repository
            .Setup(r => r.ListApprovedByAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([BuildApprovedRecommendation()]);
        var controller = new MeRecommendationsController(new GetMyRecommendationsQueryHandler(
            structure.Object,
            HabitsRecommendationsClientFactory.Create(enabled: null),
            repository.Object));

        var action = await controller.ListMine();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<MyRecommendationResponseDto>>(ok.Value));
        repository.Verify(
            r => r.ListApprovedByAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Cliente HTTP de Identidad

    [Fact]
    public async Task IdentityClient_WhenServiceReturnsOk_ReadsPreference()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "{\"enabled\":true}"))
        {
            BaseAddress = new Uri("http://identidad/")
        };
        var client = new IdentityHabitsRecommendationsClient(
            httpClient,
            NullLogger<IdentityHabitsRecommendationsClient>.Instance);

        var enabled = await client.GetAthletePfHabitsRecommendationsEnabledAsync();

        Assert.True(enabled);
    }

    [Fact]
    public async Task IdentityClient_WhenServiceFails_ReturnsNullToFailClosed()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.InternalServerError,
            string.Empty))
        {
            BaseAddress = new Uri("http://identidad/")
        };
        var client = new IdentityHabitsRecommendationsClient(
            httpClient,
            NullLogger<IdentityHabitsRecommendationsClient>.Instance);

        var enabled = await client.GetMyHabitsRecommendationsEnabledAsync();

        Assert.Null(enabled);
    }

    #endregion

    private static AthleteRecommendation BuildApprovedRecommendation()
    {
        var recommendation = AthleteRecommendation.Create(
            AthleteId,
            "Recomendación aprobada",
            "salto por debajo del promedio",
            "Texto aprobado",
            CategoryId,
            "Primera");
        recommendation.Approve();
        return recommendation;
    }

    private static (
        Mock<IStructureAthleteClient> Structure,
        Mock<ISeguimientoClient> Seguimiento,
        Mock<IAiService> Ai,
        Mock<IAthleteRecommendationRepository> Repository,
        Mock<IUnitOfWork> Uow) BuildGenerationDoubles()
    {
        var structure = new Mock<IStructureAthleteClient>();
        structure.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        structure
            .Setup(s => s.GetAthleteCategoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var seguimiento = new Mock<ISeguimientoClient>();
        seguimiento
            .Setup(s => s.GetAthleteFitnessTestsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        seguimiento
            .Setup(s => s.GetAthleteWeeklyTrendsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteWeeklyTrendsRemoteDto?)null);

        var repository = new Mock<IAthleteRecommendationRepository>();
        repository
            .Setup(r => r.ListAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<RecommendationStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        repository
            .Setup(r => r.ListByAthleteIdsAndStatusesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<RecommendationStatus>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return (structure, seguimiento, new Mock<IAiService>(), repository, new Mock<IUnitOfWork>());
    }

    private static GenerateAthleteRecommendationsCommandHandler BuildGenerationHandler(
        Mock<IStructureAthleteClient> structure,
        IIdentityHabitsRecommendationsClient identity,
        Mock<ISeguimientoClient> seguimiento,
        Mock<IAthleteRecommendationRepository> repository,
        Mock<IAiService> ai,
        Mock<IUnitOfWork> uow) =>
        new(structure.Object, identity, seguimiento.Object, repository.Object, ai.Object, uow.Object);

    private static ResilientAiService BuildNutritionService(
        IAiClient provider,
        IIdentityHabitsRecommendationsClient identity) =>
        new(
            providers: [provider],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: null,
            quotaService: null,
            logger: NullLogger<ResilientAiService>.Instance,
            identityHabitsClient: identity);

    private static (MeRecommendationsController Controller, Mock<IAthleteRecommendationRepository> Repository, Mock<IStructureAthleteClient> Structure) BuildMeController(bool enabled)
    {
        var structure = new Mock<IStructureAthleteClient>();
        structure.Setup(s => s.GetMyAthleteIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AthleteId);
        structure.SetupMyHabitsPermission();

        var repository = new Mock<IAthleteRecommendationRepository>();
        var controller = new MeRecommendationsController(new GetMyRecommendationsQueryHandler(
            structure.Object,
            HabitsRecommendationsClientFactory.Create(enabled: enabled, athleteEnabled: enabled),
            repository.Object));

        return (controller, repository, structure);
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
