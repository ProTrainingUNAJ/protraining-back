using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Estructura.Tests;

public sealed class MedicalRecordConsentTests : IAsyncDisposable
{
    private readonly SqliteConnection _sqliteConnection;

    public MedicalRecordConsentTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    [Fact]
    public void Given_AdultAthleteWithoutSignedConsent_When_ReadingOrCompletingMedicalRecord_Then_ThrowsDomainException()
    {
        // arrange: atleta adulto que aún no ha firmado el consentimiento digital
        var athlete = Athlete.CreateAdult(
            firstName: "Lucas",
            lastName: "Fernandez",
            email: "lucas.fernandez@club.com",
            phone: "+5491155551234",
            signsInitialConsent: false);

        Assert.False(athlete.HasSignedConsent);

        // act & assert: no se puede leer ni completar la ficha médica
        var readException = Assert.Throws<DomainException>(() => athlete.ReadMedicalRecord());
        Assert.Contains("consentimiento digital", readException.Message, StringComparison.OrdinalIgnoreCase);

        var writeException = Assert.Throws<DomainException>(() =>
            athlete.CompleteOrUpdateMedicalRecord(
                medicalHistory: "Sin antecedentes",
                surgeries: "Ninguna",
                pathologies: "Ninguna",
                hasPhysicalFitnessCertificate: true,
                physicalFitnessIssuedAt: new DateOnly(2026, 3, 1),
                physicalFitnessExpiresAt: new DateOnly(2027, 3, 1)));
        Assert.Contains("consentimiento digital", writeException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Given_MinorAthleteWithoutGuardianConsent_When_ReadingOrCompletingMedicalRecord_Then_ThrowsDomainException()
    {
        // arrange: atleta menor de edad sin consentimiento firmado por el tutor (Ajuste N3)
        var minorAthlete = Athlete.CreateMinorPendingConsent(
            firstName: "Mateo",
            lastName: "Gomez",
            email: "mateo.gomez@club.com",
            phone: "+5491166667777");

        Assert.True(minorAthlete.IsMinor);
        Assert.False(minorAthlete.HasSignedConsent);

        // act & assert: bloqueo para lectura y carga hasta que firme el tutor
        var readException = Assert.Throws<DomainException>(() => minorAthlete.ReadMedicalRecord());
        Assert.Contains("tutor legal", readException.Message, StringComparison.OrdinalIgnoreCase);

        var writeException = Assert.Throws<DomainException>(() =>
            minorAthlete.CompleteOrUpdateMedicalRecord(
                medicalHistory: "Asma leve en la infancia",
                surgeries: null,
                pathologies: "Asma controlada",
                hasPhysicalFitnessCertificate: true));
        Assert.Contains("tutor legal", writeException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Given_AdultAthleteWithSignedConsent_When_CompletingAndReadingMedicalRecord_Then_StoresAllMedicalData()
    {
        // arrange
        var athlete = Athlete.CreateAdult(
            firstName: "Martin",
            lastName: "Palermo",
            email: "martin@club.com",
            phone: "+5491122334455",
            signsInitialConsent: false);

        athlete.RegisterAdultConsent(allowsPhysicalTraining: true, allowsHabitsRecommendations: true);
        Assert.True(athlete.HasSignedConsent);

        var issuedAt = new DateOnly(2026, 2, 15);
        var expiresAt = new DateOnly(2027, 2, 15);

        // act
        var record = athlete.CompleteOrUpdateMedicalRecord(
            medicalHistory: "Antecedente de esguince de tobillo derecho (2024)",
            surgeries: "Reconstrucción de LCA rodilla izquierda (2022)",
            pathologies: "Sin patologías cardiovasculares",
            hasPhysicalFitnessCertificate: true,
            physicalFitnessIssuedAt: issuedAt,
            physicalFitnessExpiresAt: expiresAt,
            physicalFitnessNotes: "Ergometría de 12 derivaciones normal");

        var readRecord = athlete.ReadMedicalRecord();

        // assert
        Assert.NotNull(readRecord);
        Assert.Equal(record.Id, readRecord.Id);
        Assert.Equal(athlete.Id, readRecord.AthleteId);
        Assert.Equal("Antecedente de esguince de tobillo derecho (2024)", readRecord.MedicalHistory);
        Assert.Equal("Reconstrucción de LCA rodilla izquierda (2022)", readRecord.Surgeries);
        Assert.Equal("Sin patologías cardiovasculares", readRecord.Pathologies);
        Assert.True(readRecord.HasPhysicalFitnessCertificate);
        Assert.Equal(issuedAt, readRecord.PhysicalFitnessIssuedAt);
        Assert.Equal(expiresAt, readRecord.PhysicalFitnessExpiresAt);
        Assert.Equal("Ergometría de 12 derivaciones normal", readRecord.PhysicalFitnessNotes);
    }

    [Fact]
    public void Given_MinorAthleteWithSignedGuardianConsent_When_CompletingAndReadingMedicalRecord_Then_Succeeds()
    {
        // arrange: menor que recibe la firma del tutor legal (Ajuste N3)
        var minorAthlete = Athlete.CreateMinorPendingConsent(
            firstName: "Sofia",
            lastName: "Martinez",
            email: "sofia.martinez@club.com",
            phone: "+5491144445555");

        minorAthlete.RegisterGuardianConsent(
            guardianName: "Laura Martinez",
            guardianEmail: "laura.martinez@email.com",
            guardianPhone: "+5491199998888",
            allowsPhysicalTraining: true,
            allowsHabitsRecommendations: false);

        Assert.True(minorAthlete.HasSignedConsent);

        // act
        minorAthlete.CompleteOrUpdateMedicalRecord(
            medicalHistory: "Sin antecedentes relevantes",
            surgeries: "Apendicectomía (2021)",
            pathologies: "Alergia estacional al polen",
            hasPhysicalFitnessCertificate: true,
            physicalFitnessIssuedAt: new DateOnly(2026, 3, 10),
            physicalFitnessExpiresAt: new DateOnly(2027, 3, 10),
            physicalFitnessNotes: "Apto competitivo juvenil");

        var readRecord = minorAthlete.ReadMedicalRecord();

        // assert
        Assert.NotNull(readRecord);
        Assert.Equal("Apendicectomía (2021)", readRecord.Surgeries);
        Assert.Equal("Alergia estacional al polen", readRecord.Pathologies);
        Assert.True(readRecord.HasPhysicalFitnessCertificate);
    }

    [Fact]
    public void Given_SignedDigitalConsent_When_UpdatingHabitsOrAttemptingReSign_Then_ConsentRecordRemainsImmutable()
    {
        // arrange
        var athlete = Athlete.CreateMinor(
            firstName: "Tomas",
            lastName: "Rojas",
            email: "tomas.rojas@club.com",
            phone: "+5491133332222",
            guardianName: "Carlos Rojas",
            guardianEmail: "carlos.rojas@email.com",
            guardianPhone: "+5491177776666",
            allowsPhysicalTraining: true,
            allowsHabitsRecommendations: false);

        var originalConsentDate = athlete.DigitalConsent!.ConsentDate;

        // act: cambiar permiso secundario de hábitos no debe alterar la fecha ni firma original
        athlete.UpdateHabitsPermission(allowsHabitsRecommendations: true);

        // assert
        Assert.True(athlete.AllowsHabitsRecommendations);
        Assert.Equal(originalConsentDate, athlete.DigitalConsent.ConsentDate);
        Assert.Equal("Carlos Rojas", athlete.DigitalConsent.GuardianName);

        // intentar volver a firmar un consentimiento ya firmado debe ser rechazado por inmutabilidad
        Assert.Throws<DomainException>(() =>
            athlete.RegisterGuardianConsent(
                guardianName: "Otro Tutor",
                guardianEmail: "otro@email.com",
                guardianPhone: "+5491100001111",
                allowsPhysicalTraining: true,
                allowsHabitsRecommendations: true));
    }

    [Fact]
    public void Given_InvalidPhysicalFitnessDates_When_CompletingMedicalRecord_Then_ThrowsDomainException()
    {
        var athlete = Athlete.CreateAdult(
            firstName: "Julian",
            lastName: "Alvarez",
            email: "julian@club.com",
            phone: "+5491188889999",
            signsInitialConsent: true);

        Assert.Throws<DomainException>(() =>
            athlete.CompleteOrUpdateMedicalRecord(
                medicalHistory: "Normal",
                surgeries: null,
                pathologies: null,
                hasPhysicalFitnessCertificate: true,
                physicalFitnessIssuedAt: new DateOnly(2026, 10, 10),
                physicalFitnessExpiresAt: new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public async Task Given_AthleteWithConsentAndMedicalRecord_When_SavedInDatabase_Then_PersistsInMedicalRecordsAndDigitalConsentsTables()
    {
        // arrange: contexto de EF con el PF autenticado en ICurrentUser
        var pfId = Guid.NewGuid();
        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.EnsureCreatedAsync();

        var athlete = Athlete.CreateMinor(
            firstName: "Enzo",
            lastName: "Fernandez",
            email: "enzo@club.com",
            phone: "+5491122223333",
            guardianName: "Raul Fernandez",
            guardianEmail: "raul@email.com",
            guardianPhone: "+5491144445555",
            allowsPhysicalTraining: true,
            allowsHabitsRecommendations: true);

        athlete.CompleteOrUpdateMedicalRecord(
            medicalHistory: "Control cardiológico anual aprobado",
            surgeries: "Ninguna",
            pathologies: "Ninguna",
            hasPhysicalFitnessCertificate: true,
            physicalFitnessIssuedAt: new DateOnly(2026, 3, 1),
            physicalFitnessExpiresAt: new DateOnly(2027, 3, 1),
            physicalFitnessNotes: "Apto físico presentado");

        // act: AppDbContext asigna automáticamente PfId y CreatedAt en todas las entidades nuevas
        dbContext.Athletes.Add(athlete);
        await dbContext.SaveChangesAsync();

        // assert: verificar lectura mediante el filtro global del mismo PF
        await using var verifyContext = CreateDbContextForPf(pfId);
        var persistedRecord = await verifyContext.MedicalRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.AthleteId == athlete.Id);

        var persistedConsent = await verifyContext.DigitalConsents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.AthleteId == athlete.Id);

        Assert.NotNull(persistedRecord);
        Assert.Equal(pfId, persistedRecord.PfId);
        Assert.Equal("Control cardiológico anual aprobado", persistedRecord.MedicalHistory);
        Assert.True(persistedRecord.HasPhysicalFitnessCertificate);
        Assert.Equal(new DateOnly(2026, 3, 1), persistedRecord.PhysicalFitnessIssuedAt);
        Assert.Equal(new DateOnly(2027, 3, 1), persistedRecord.PhysicalFitnessExpiresAt);

        Assert.NotNull(persistedConsent);
        Assert.Equal(pfId, persistedConsent.PfId);
        Assert.True(persistedConsent.IsSigned);
        Assert.True(persistedConsent.IsMinor);
        Assert.Equal("Raul Fernandez", persistedConsent.GuardianName);

        // assert aislamiento: otro PF distinto no puede leer la ficha médica ni el consentimiento
        await using var otherPfContext = CreateDbContextForPf(Guid.NewGuid());
        var leakedRecord = await otherPfContext.MedicalRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.AthleteId == athlete.Id);
        Assert.Null(leakedRecord);
    }

    private AppDbContext CreateDbContextForPf(Guid pfId)
    {
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        return new AppDbContext(options, currentUserMock.Object);
    }

    public async ValueTask DisposeAsync()
    {
        await _sqliteConnection.DisposeAsync();
    }
}
