using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.Interfaces.Services;
using Application.UseCases.Athletes.PurgePendingAthletes;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Estructura.Tests.Integration;

/// <summary>
/// Pruebas de integración de punta a punta (E2E) para el flujo completo de borrado y purga de atletas (KAN-501).
/// Cubre: Creación con datos asociados -> Baja lógica -> Ejecución del job de purga -> Despacho a los 5 servicios -> Verificación de eliminación total.
/// </summary>
public sealed class AthletePurgeE2ETests : IAsyncDisposable
{
    private sealed class SimulatedExternalServiceStore
    {
        private static readonly string[] TargetServices = ["Identidad", "Entrenamiento", "Seguimiento", "Asistente"];
        private readonly Dictionary<string, HashSet<Guid>> _serviceData = new()
        {
            ["Identidad"] = [],
            ["Entrenamiento"] = [],
            ["Seguimiento"] = [],
            ["Asistente"] = []
        };

        public List<string> DispatchCallsRecorded { get; } = [];

        public void SeedAthleteAcrossAllExternalServices(Guid athleteId)
        {
            foreach (var service in TargetServices)
            {
                _serviceData[service].Add(athleteId);
            }
        }

        public bool ContainsAthlete(string serviceName, Guid athleteId) =>
            _serviceData.TryGetValue(serviceName, out var store) && store.Contains(athleteId);

        public ServicePurgeResult ProcessPurge(string serviceName, Guid athleteId)
        {
            DispatchCallsRecorded.Add(serviceName);

            if (!_serviceData.TryGetValue(serviceName, out var store))
            {
                return new ServicePurgeResult(serviceName, false, 400, "Servicio desconocido");
            }

            // Si el atleta existía se remueve (200 OK); si no existía, responde 404 (idempotencia)
            return store.Remove(athleteId)
                ? new ServicePurgeResult(serviceName, true, 200)
                : new ServicePurgeResult(serviceName, true, 404);
        }
    }

    private readonly SimulatedExternalServiceStore _simulatedStore = new();
    private readonly Mock<IExternalServicePurgeClient> _mockPurgeClient = new();
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly HttpClient _clientPf;

    public AthletePurgeE2ETests()
    {
        _mockPurgeClient
            .Setup(c => c.PurgeAthleteInServiceAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string serviceName, string baseUrl, Guid athleteId, CancellationToken ct) =>
                _simulatedStore.ProcessPurge(serviceName, athleteId));

        _factory = new EstructuraTestApplicationFactory(
            customExternalPurgeClient: _mockPurgeClient.Object);

        _clientPf = _factory.CreateAuthenticatedClient(_pfId);
    }

    private async Task<Guid> CreateAthleteAsync(string email)
    {
        var createRequest = new CreateAthleteRequest(
            FirstName: "Atleta",
            LastName: "Test",
            Email: email,
            Phone: "+5491188990011",
            IsMinor: false);

        var createResponse = await _clientPf.PostAsJsonAsync("/api/v1/athletes", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdAthlete = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(createdAthlete);
        return createdAthlete.Id;
    }

    private async Task<Guid> CreateAndSoftDeleteAthleteAsync(string email)
    {
        var athleteId = await CreateAthleteAsync(email);
        var deleteResponse = await _clientPf.DeleteAsync($"/api/v1/athletes/{athleteId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        return athleteId;
    }

    [Fact]
    public async Task Given_AthleteWithDataInAll5Services_When_SoftDeletedAndPurgeRuns_Then_DisappearsFromAllServices()
    {
        // -----------------------------------------------------------------------------------------
        // 1. ARRANGE: Crear atleta con datos asociados en los 5 servicios (Criterio KAN-501)
        // -----------------------------------------------------------------------------------------

        // 1.a) Servicio 1 (Estructura): Crear Atleta mediante API
        var athleteId = await CreateAthleteAsync("federico.gomez.e2e@test.com");

        // 1.b) Asociar categoría y ficha médica en Estructura
        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            var organization = Organization.Create("Club Atlético Test E2E", "Organización para prueba E2E");
            dbContext.Organizations.Add(organization);

            var category = Category.Create(organization.Id, "Plantel Superior E2E", "Categoría para prueba E2E");
            dbContext.Categories.Add(category);

            var athleteCategory = AthleteCategory.Assign(athleteId, category.Id);
            dbContext.AthleteCategories.Add(athleteCategory);

            var medicalRecord = MedicalRecord.Create(
                athleteId,
                "Apto físico vigente",
                "Sin antecedentes coronarios",
                "0+",
                true);
            dbContext.MedicalRecords.Add(medicalRecord);

            var digitalConsent = DigitalConsent.RegisterForAdult(
                athleteId,
                allowsPhysicalTraining: true,
                allowsHabitsRecommendations: true,
                allowsProfilePhoto: true);
            dbContext.DigitalConsents.Add(digitalConsent);

            await dbContext.SaveChangesAsync();
        }

        // 1.c) Servicios 2, 3, 4 y 5 (Identidad, Entrenamiento, Seguimiento, Asistente): Sembrar datos
        _simulatedStore.SeedAthleteAcrossAllExternalServices(athleteId);
        Assert.True(_simulatedStore.ContainsAthlete("Identidad", athleteId));
        Assert.True(_simulatedStore.ContainsAthlete("Entrenamiento", athleteId));
        Assert.True(_simulatedStore.ContainsAthlete("Seguimiento", athleteId));
        Assert.True(_simulatedStore.ContainsAthlete("Asistente", athleteId));

        // -----------------------------------------------------------------------------------------
        // 2. ACT (Paso 1): Dar de baja lógica al atleta
        // -----------------------------------------------------------------------------------------
        var deleteResponse = await _clientPf.DeleteAsync($"/api/v1/athletes/{athleteId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verificar estado intermedio: no aparece en endpoints activos de Estructura
        var getActiveResponse = await _clientPf.GetAsync($"/api/v1/athletes/{athleteId}");
        Assert.Equal(HttpStatusCode.NotFound, getActiveResponse.StatusCode);

        // Verificar que se registró la purga pendiente
        using (var dbCheck = _factory.CreateDbContext())
        {
            var pendingPurge = await dbCheck.PendingPurges
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.AthleteId == athleteId);

            Assert.NotNull(pendingPurge);
            Assert.Equal(PendingPurge.PendingStatus, pendingPurge.Status);
        }

        // -----------------------------------------------------------------------------------------
        // 2. ACT (Paso 2): Correr el Job de Purga (RetentionDays = 0 para elegibilidad inmediata)
        // -----------------------------------------------------------------------------------------
        using (var scope = _factory.Services.CreateScope())
        {
            var purgeHandler = scope.ServiceProvider.GetRequiredService<ProcessPendingPurgesCommandHandler>();
            var purgeResult = await purgeHandler.HandleAsync(
                new ProcessPendingPurgesCommand(RetentionDays: 0),
                CancellationToken.None);

            Assert.True(purgeResult.TotalEvaluated >= 1);
            Assert.True(purgeResult.PurgedSuccessfully >= 1);
            Assert.Equal(0, purgeResult.FailedOrPostponed);
        }

        // -----------------------------------------------------------------------------------------
        // 3. ASSERT: Verificación total en los 5 servicios (Criterio de aceptación)
        // -----------------------------------------------------------------------------------------

        // 3.a) Verificación en los 4 servicios externos: Los almacenes quedaron vacíos para este atleta
        Assert.False(_simulatedStore.ContainsAthlete("Identidad", athleteId));
        Assert.False(_simulatedStore.ContainsAthlete("Entrenamiento", athleteId));
        Assert.False(_simulatedStore.ContainsAthlete("Seguimiento", athleteId));
        Assert.False(_simulatedStore.ContainsAthlete("Asistente", athleteId));

        // Se comprobó que el dispatcher invocó a los 4 servicios en orden
        Assert.Contains("Identidad", _simulatedStore.DispatchCallsRecorded);
        Assert.Contains("Entrenamiento", _simulatedStore.DispatchCallsRecorded);
        Assert.Contains("Seguimiento", _simulatedStore.DispatchCallsRecorded);
        Assert.Contains("Asistente", _simulatedStore.DispatchCallsRecorded);

        // 3.b) Verificación en Estructura (Servicio 5):
        using (var dbFinal = _factory.CreateDbContext())
        {
            // Atleta eliminado físicamente
            var dbAthlete = await dbFinal.Athletes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == athleteId);
            Assert.Null(dbAthlete);

            // Categorías asociadas eliminadas
            var dbCategories = await dbFinal.AthleteCategories
                .IgnoreQueryFilters()
                .Where(ac => ac.AthleteId == athleteId)
                .ToListAsync();
            Assert.Empty(dbCategories);

            // Ficha médica eliminada
            var dbMedical = await dbFinal.MedicalRecords
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.AthleteId == athleteId);
            Assert.Null(dbMedical);

            // Consentimiento digital eliminado
            var dbConsent = await dbFinal.DigitalConsents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.AthleteId == athleteId);
            Assert.Null(dbConsent);

            // Registro de purga marcado como Procesado con timestamp
            var finalPurge = await dbFinal.PendingPurges
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.AthleteId == athleteId);
            Assert.NotNull(finalPurge);
            Assert.Equal(PendingPurge.ProcessedStatus, finalPurge.Status);
            Assert.NotNull(finalPurge.ProcessedAt);
        }

        // 3.c) Verificación en API: GET devuelve 404 definitivo
        var finalGetResponse = await _clientPf.GetAsync($"/api/v1/athletes/{athleteId}");
        Assert.Equal(HttpStatusCode.NotFound, finalGetResponse.StatusCode);
    }

    [Fact]
    public async Task Given_ExternalServiceFails_When_PurgeRuns_Then_AbortsAndPreservesLocalDataForRetry()
    {
        // Arrange: Atleta dado de baja
        var athleteId = await CreateAndSoftDeleteAthleteAsync("matias.rojas.e2e@test.com");

        // Simulamos que Seguimiento falla con error 500
        _mockPurgeClient
            .Setup(c => c.PurgeAthleteInServiceAsync(
                "Seguimiento",
                It.IsAny<string>(),
                athleteId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePurgeResult("Seguimiento", false, 500, "Internal Server Error"));

        // Act: Correr job de purga
        using (var scope = _factory.Services.CreateScope())
        {
            var purgeHandler = scope.ServiceProvider.GetRequiredService<ProcessPendingPurgesCommandHandler>();
            var result = await purgeHandler.HandleAsync(
                new ProcessPendingPurgesCommand(RetentionDays: 0),
                CancellationToken.None);

            Assert.Equal(1, result.FailedOrPostponed);
            Assert.Equal(0, result.PurgedSuccessfully);
        }

        // Assert: En Estructura los datos locales NO fueron borrados para permitir reintentos consistentes
        using (var dbContext = _factory.CreateDbContext())
        {
            var dbAthlete = await dbContext.Athletes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == athleteId);
            Assert.NotNull(dbAthlete); // El atleta sigue existiendo en BD local

            var pendingPurge = await dbContext.PendingPurges
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.AthleteId == athleteId);
            Assert.NotNull(pendingPurge);
            Assert.Equal(PendingPurge.PendingStatus, pendingPurge.Status); // Sigue pendiente
            Assert.Null(pendingPurge.ProcessedAt);
        }
    }

    [Fact]
    public async Task Given_ExternalServicesReturn404_When_PurgeRuns_Then_TreatsAsIdempotentSuccessAndCompletesPurge()
    {
        // Arrange: Atleta que no existía en servicios externos (ej. recién registrado y borrado antes de interactuar)
        var athleteId = await CreateAndSoftDeleteAthleteAsync("cristian.medina.e2e@test.com");

        // No sembramos nada en SimulatedExternalServiceStore -> responderá 404 para los 4 servicios

        // Act
        using (var scope = _factory.Services.CreateScope())
        {
            var purgeHandler = scope.ServiceProvider.GetRequiredService<ProcessPendingPurgesCommandHandler>();
            var result = await purgeHandler.HandleAsync(
                new ProcessPendingPurgesCommand(RetentionDays: 0),
                CancellationToken.None);

            Assert.Equal(1, result.PurgedSuccessfully);
            Assert.Equal(0, result.FailedOrPostponed);
        }

        // Assert: La purga se completa correctamente por idempotencia
        using (var dbContext = _factory.CreateDbContext())
        {
            var dbAthlete = await dbContext.Athletes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == athleteId);
            Assert.Null(dbAthlete);

            var pendingPurge = await dbContext.PendingPurges
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.AthleteId == athleteId);
            Assert.NotNull(pendingPurge);
            Assert.Equal(PendingPurge.ProcessedStatus, pendingPurge.Status);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
