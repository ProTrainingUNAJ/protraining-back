using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Estructura.Tests.Integration;

public sealed class MedicalRecordEndpointsTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId;
    private readonly Guid _pfAUserId;
    private readonly Guid _pfBId;
    private readonly HttpClient _clientPfA;
    private readonly HttpClient _clientPfB;

    public MedicalRecordEndpointsTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfAId = Guid.NewGuid();
        _pfAUserId = Guid.NewGuid();
        _pfBId = Guid.NewGuid();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId, role: "PreparadorFisico", userId: _pfAUserId);
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId, role: "PreparadorFisico");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Given_PfAttemptingToReadMedicalRecord_When_JustificationIsMissingOrEmpty_Then_Returns400BadRequestAndDoesNotInsertAuditLog(
        string? invalidJustification)
    {
        // arrange: crear un atleta con consentimiento firmado
        var createResponse = await _clientPfA.PostAsJsonAsync(
            "/api/v1/athletes",
            new CreateAthleteRequest("Lucas", "Fernandez", "lucas.f@club.com", "+5491112345678", IsMinor: false));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var athlete = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);

        var url = invalidJustification is null
            ? $"/api/v1/athletes/{athlete.Id}/medical-record"
            : $"/api/v1/athletes/{athlete.Id}/medical-record?justification={Uri.EscapeDataString(invalidJustification)}";

        // act
        var response = await _clientPfA.GetAsync(url);

        // assert: recibe HTTP 400 Bad Request indicando que la justificación es obligatoria
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("justificación", problem.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // verificar que NO se insertó ningún registro en auditoría
        // Se usa IgnoreQueryFilters porque el DbContext de verificación en el test corre fuera de un request HTTP y no tiene un PF activo.
        using var dbContext = _factory.CreateDbContext();
        var auditLogsCount = await dbContext.MedicalRecordAuditLogs
            .IgnoreQueryFilters()
            .CountAsync(log => log.AthleteId == athlete.Id);
        Assert.Equal(0, auditLogsCount);
    }

    [Fact]
    public async Task Given_PfWithValidJustification_When_UpdatingAndReadingMedicalRecord_Then_ReturnsMedicalDataAndInsertsImmutableAuditLogs()
    {
        // arrange: crear atleta adulto con consentimiento firmado
        var createResponse = await _clientPfA.PostAsJsonAsync(
            "/api/v1/athletes",
            new CreateAthleteRequest("Martin", "Palermo", "martin.p@club.com", "+5491122334455", IsMinor: false));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var athlete = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);

        // act 1: cargar ficha médica con motivo válido (Create)
        var putRequest = new UpdateMedicalRecordRequest(
            Justification: "Carga inicial de apto físico y antecedentes deportivos",
            MedicalHistory: "Esguince de tobillo grado II en 2024",
            Surgeries: "Reconstrucción LCA rodilla izquierda (2022)",
            Pathologies: "Sin patologías cardiovasculares",
            HasPhysicalFitnessCertificate: true,
            PhysicalFitnessIssuedAt: new DateOnly(2026, 3, 1),
            PhysicalFitnessExpiresAt: new DateOnly(2027, 3, 1),
            PhysicalFitnessNotes: "Ergometría de 12 derivaciones aprobada");

        var putResponse = await _clientPfA.PutAsJsonAsync($"/api/v1/athletes/{athlete.Id}/medical-record", putRequest);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var updatedRecord = await putResponse.Content.ReadFromJsonAsync<MedicalRecordDto>();
        Assert.NotNull(updatedRecord);
        Assert.NotNull(updatedRecord.Id);
        Assert.Equal(athlete.Id, updatedRecord.AthleteId);
        Assert.Equal("Esguince de tobillo grado II en 2024", updatedRecord.MedicalHistory);
        Assert.Equal("Reconstrucción LCA rodilla izquierda (2022)", updatedRecord.Surgeries);
        Assert.True(updatedRecord.HasPhysicalFitnessCertificate);

        // act 2: consultar ficha médica con motivo válido (Read)
        var justification = "Revisión médica previa al bloque de fuerza máxima";
        var getResponse = await _clientPfA.GetAsync(
            $"/api/v1/athletes/{athlete.Id}/medical-record?justification={Uri.EscapeDataString(justification)}");

        // assert: recibe HTTP 200 OK con los datos médicos
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var readRecord = await getResponse.Content.ReadFromJsonAsync<MedicalRecordDto>();
        Assert.NotNull(readRecord);
        Assert.Equal(updatedRecord.Id, readRecord.Id);
        Assert.Equal(athlete.Id, readRecord.AthleteId);
        Assert.Equal("Esguince de tobillo grado II en 2024", readRecord.MedicalHistory);
        Assert.Equal("Reconstrucción LCA rodilla izquierda (2022)", readRecord.Surgeries);
        Assert.Equal("Sin patologías cardiovasculares", readRecord.Pathologies);
        Assert.True(readRecord.HasPhysicalFitnessCertificate);
        Assert.Equal(new DateOnly(2026, 3, 1), readRecord.PhysicalFitnessIssuedAt);
        Assert.Equal(new DateOnly(2027, 3, 1), readRecord.PhysicalFitnessExpiresAt);
        Assert.Equal("Ergometría de 12 derivaciones aprobada", readRecord.PhysicalFitnessNotes);

        // assert: se insertaron los registros inmutables en auditoría (Create y Read)
        var logsResponse = await _clientPfA.GetAsync($"/api/v1/athletes/{athlete.Id}/medical-record/audit-logs");
        Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
        var auditLogs = await logsResponse.Content.ReadFromJsonAsync<List<MedicalRecordAuditLogDto>>();
        Assert.NotNull(auditLogs);
        Assert.Equal(2, auditLogs.Count);

        Assert.Contains(auditLogs, log =>
            log.AthleteId == athlete.Id &&
            log.AccessedByUserId == _pfAUserId &&
            log.Action == MedicalRecordAuditLog.CreateAction &&
            log.Justification == "Carga inicial de apto físico y antecedentes deportivos");

        Assert.Contains(auditLogs, log =>
            log.AthleteId == athlete.Id &&
            log.AccessedByUserId == _pfAUserId &&
            log.Action == MedicalRecordAuditLog.ReadAction &&
            log.Justification == justification);
    }

    [Fact]
    public async Task Given_MinorAthletePendingGuardianConsent_When_PfAttemptsToReadOrUpdateMedicalRecord_Then_Returns400BadRequestAndDoesNotInsertAuditLog()
    {
        // arrange: crear un atleta menor y simular que su tutor legal aún no ha firmado el consentimiento digital
        var createResponse = await _clientPfA.PostAsJsonAsync(
            "/api/v1/athletes",
            new CreateAthleteRequest(
                FirstName: "Mateo",
                LastName: "Gomez",
                Email: "mateo@club.com",
                Phone: "+5491166667777",
                IsMinor: true,
                GuardianName: "Laura Gomez",
                GuardianEmail: "laura@email.com",
                GuardianPhone: "+5491199998888",
                AllowsPhysicalTraining: true,
                AllowsHabitsRecommendations: false));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var minor = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(minor);

        using (var seedContext = _factory.CreateDbContext())
        {
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM digital_consents WHERE athlete_id = {minor.Id}");
        }

        // act: intentar leer ficha médica con justificación válida pero sin consentimiento válido
        var getResponse = await _clientPfA.GetAsync(
            $"/api/v1/athletes/{minor.Id}/medical-record?justification={Uri.EscapeDataString("Consulta rutinaria")}");

        // assert: bloqueado con 400 Bad Request y sin registro de auditoría
        Assert.Equal(HttpStatusCode.BadRequest, getResponse.StatusCode);
        var problem = await getResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("tutor legal", problem.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Se usa IgnoreQueryFilters porque el DbContext de verificación en el test corre fuera de un request HTTP y no tiene un PF activo.
        using var verifyContext = _factory.CreateDbContext();
        var logsCount = await verifyContext.MedicalRecordAuditLogs
            .IgnoreQueryFilters()
            .CountAsync(log => log.AthleteId == minor.Id);
        Assert.Equal(0, logsCount);
    }

    [Fact]
    public async Task Given_AthleteOwnedByPfB_When_PfAAttemptsToReadOrUpdateMedicalRecord_Then_Returns404NotFound()
    {
        // arrange: PF-B crea un atleta
        var createResponse = await _clientPfB.PostAsJsonAsync(
            "/api/v1/athletes",
            new CreateAthleteRequest("Esteban", "Brito", "esteban@club.com", "+5491199998888", IsMinor: false));
        var athleteB = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athleteB);

        // act: PF-A intenta leer y modificar la ficha médica del atleta de PF-B
        var getResponse = await _clientPfA.GetAsync(
            $"/api/v1/athletes/{athleteB.Id}/medical-record?justification={Uri.EscapeDataString("Motivo válido")}");
        var putResponse = await _clientPfA.PutAsJsonAsync(
            $"/api/v1/athletes/{athleteB.Id}/medical-record",
            new UpdateMedicalRecordRequest("Motivo válido", "Historial", null, null, true));
        var logsResponse = await _clientPfA.GetAsync(
            $"/api/v1/athletes/{athleteB.Id}/medical-record/audit-logs");

        // assert: aislamiento multi-tenant responde 404 Not Found
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, logsResponse.StatusCode);
    }

    [Fact]
    public async Task Given_AuditLogSaveFails_When_ReadingMedicalRecord_Then_ThrowsAndNeverReturnsMedicalData()
    {
        // arrange: si falla el guardado del registro de auditoría en SaveChangesAsync, el handler no debe devolver la ficha médica
        var athlete = Athlete.CreateAdult("Diego", "Milito", "diego@club.com", "+5491133445566", signsInitialConsent: true);
        athlete.CompleteOrUpdateMedicalRecord(
            medicalHistory: "Antecedente confidencial",
            surgeries: null,
            pathologies: null,
            hasPhysicalFitnessCertificate: true);

        var athleteRepositoryMock = new Moq.Mock<Application.Interfaces.Persistence.IAthleteRepository>();
        athleteRepositoryMock
            .Setup(repo => repo.GetByIdWithMedicalRecordAsync(athlete.Id, Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);

        var auditLogRepositoryMock = new Moq.Mock<Application.Interfaces.Persistence.IMedicalRecordAuditLogRepository>();
        var currentUserMock = new Moq.Mock<Application.Interfaces.Services.ICurrentUser>();
        currentUserMock.Setup(u => u.UserId).Returns(_pfAUserId);
        currentUserMock.Setup(u => u.PfId).Returns(_pfAId);

        var unitOfWorkMock = new Moq.Mock<Application.Interfaces.Persistence.IUnitOfWork>();
        unitOfWorkMock
            .Setup(uow => uow.SaveChangesAsync(Moq.It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fallo simulado al persistir el registro de auditoría médica."));

        var handler = new Application.UseCases.Athletes.ReadMedicalRecord.ReadMedicalRecordCommandHandler(
            athleteRepositoryMock.Object,
            auditLogRepositoryMock.Object,
            currentUserMock.Object,
            unitOfWorkMock.Object);

        // act & assert: falla la operación y nunca se entrega el MedicalRecordDto al llamador
        MedicalRecordDto? leakedRecord = null;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            leakedRecord = await handler.HandleAsync(
                new Application.UseCases.Athletes.ReadMedicalRecord.ReadMedicalRecordCommand(
                    athlete.Id,
                    "Consulta previa a entrenamiento"));
        });

        Assert.Null(leakedRecord);
    }

    public async ValueTask DisposeAsync()
    {
        _clientPfA.Dispose();
        _clientPfB.Dispose();
        await _factory.DisposeAsync();
    }
}
