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
        // Almacenes en memoria simulando las entidades de los 4 servicios externos asociados al athleteId
        public HashSet<Guid> IdentidadAthletes { get; } = [];
        public HashSet<Guid> EntrenamientoAthletes { get; } = [];
        public HashSet<Guid> SeguimientoAthletes { get; } = [];
        public HashSet<Guid> AsistenteAthletes { get; } = [];

        public List<string> DispatchCallsRecorded { get; } = [];

        public void SeedAthleteAcrossAllExternalServices(Guid athleteId)
        {
            IdentidadAthletes.Add(athleteId);
            EntrenamientoAthletes.Add(athleteId);
            SeguimientoAthletes.Add(athleteId);
            AsistenteAthletes.Add(athleteId);
        }

        public ServicePurgeResult ProcessPurge(string serviceName, Guid athleteId)
        {
            DispatchCallsRecorded.Add(serviceName);

            return serviceName switch
            {
                "Identidad" => IdentidadAthletes.Remove(athleteId)
                    ? new ServicePurgeResult("Identidad", true, 200)
                    : new ServicePurgeResult("Identidad", true, 404), // Idempotente

                "Entrenamiento" => EntrenamientoAthletes.Remove(athleteId)
                    ? new ServicePurgeResult("Entrenamiento", true, 200)
                    : new ServicePurgeResult("Entrenamiento", true, 404),

                "Seguimiento" => SeguimientoAthletes.Remove(athleteId)
                    ? new ServicePurgeResult("Seguimiento", true, 200)
                    : new ServicePurgeResult("Seguimiento", true, 404),

                "Asistente" => AsistenteAthletes.Remove(athleteId)
                    ? new ServicePurgeResult("Asistente", true, 200)
                    : new ServicePurgeResult("Asistente", true, 404),

                _ => new ServicePurgeResult(serviceName, false, 400, "Servicio desconocido")
            };
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

    [Fact]
    public async Task Given_AthleteWithDataInAll5Services_When_SoftDeletedAndPurgeRuns_Then_DisappearsFromAllServices()
    {
        // -----------------------------------------------------------------------------------------
        // 1. ARRANGE: Crear atleta con datos asociados en los 5 servicios (Criterio KAN-501)
        // -----------------------------------------------------------------------------------------

        // 1.a) Servicio 1 (Estructura): Crear Atleta mediante API
        var createRequest = new CreateAthleteRequest(
            FirstName: "Federico",
            LastName: "Gomez",
            Email: "federico.gomez.e2e@test.com",
            Phone: "+5491188990011",
            IsMinor: false);

        var createResponse = await _clientPf.PostAsJsonAsync("/api/v1/athletes", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdAthlete = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(createdAthlete);
        var athleteId = createdAthlete.Id;

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
        Assert.Contains(athleteId, _simulatedStore.IdentidadAthletes);
        Assert.Contains(athleteId, _simulatedStore.EntrenamientoAthletes);
        Assert.Contains(athleteId, _simulatedStore.SeguimientoAthletes);
        Assert.Contains(athleteId, _simulatedStore.AsistenteAthletes);

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
            Assert.Equal("Pending", pendingPurge.Status);
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
        Assert.DoesNotContain(athleteId, _simulatedStore.IdentidadAthletes);
        Assert.DoesNotContain(athleteId, _simulatedStore.EntrenamientoAthletes);
        Assert.DoesNotContain(athleteId, _simulatedStore.SeguimientoAthletes);
        Assert.DoesNotContain(athleteId, _simulatedStore.AsistenteAthletes);

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
        var createRequest = new CreateAthleteRequest(
            FirstName: "Matias",
            LastName: "Rojas",
            Email: "matias.rojas.e2e@test.com",
            Phone: "+5491144556677",
            IsMinor: false);

        var createResponse = await _clientPf.PostAsJsonAsync("/api/v1/athletes", createRequest);
        var createdAthlete = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(createdAthlete);
        var athleteId = createdAthlete.Id;

        await _clientPf.DeleteAsync($"/api/v1/athletes/{athleteId}");

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
            Assert.Equal("Pending", pendingPurge.Status); // Sigue pendiente
            Assert.Null(pendingPurge.ProcessedAt);
        }
    }

    [Fact]
    public async Task Given_ExternalServicesReturn404_When_PurgeRuns_Then_TreatsAsIdempotentSuccessAndCompletesPurge()
    {
        // Arrange: Atleta que no existía en servicios externos (ej. recién registrado y borrado antes de interactuar)
        var createRequest = new CreateAthleteRequest(
            FirstName: "Cristian",
            LastName: "Medina",
            Email: "cristian.medina.e2e@test.com",
            Phone: "+5491133221100",
            IsMinor: false);

        var createResponse = await _clientPf.PostAsJsonAsync("/api/v1/athletes", createRequest);
        var createdAthlete = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(createdAthlete);
        var athleteId = createdAthlete.Id;

        await _clientPf.DeleteAsync($"/api/v1/athletes/{athleteId}");

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
