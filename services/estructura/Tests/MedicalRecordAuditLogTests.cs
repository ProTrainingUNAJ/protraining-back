using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Estructura.Tests;

public sealed class MedicalRecordAuditLogTests : IAsyncDisposable
{
    private readonly SqliteConnection _sqliteConnection;

    public MedicalRecordAuditLogTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    [Fact]
    public void Given_ValidParameters_When_CreatingMedicalRecordAuditLogs_Then_SucceedsAndTrimsFields()
    {
        var athleteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var readLog = MedicalRecordAuditLog.CreateRead(
            athleteId,
            userId,
            "  Consulta previa al entrenamiento de fuerza  ");

        var createLog = MedicalRecordAuditLog.CreateCreation(
            athleteId,
            userId,
            "Carga inicial de ficha médica y apto físico");

        var updateLog = MedicalRecordAuditLog.CreateUpdate(
            athleteId,
            userId,
            "Actualización de fecha de vencimiento de ergometría");

        Assert.Equal(athleteId, readLog.AthleteId);
        Assert.Equal(userId, readLog.AccessedByUserId);
        Assert.Equal(MedicalRecordAuditLog.ReadAction, readLog.Action);
        Assert.Equal("Consulta previa al entrenamiento de fuerza", readLog.Justification);

        Assert.Equal(MedicalRecordAuditLog.CreateAction, createLog.Action);
        Assert.Equal(MedicalRecordAuditLog.UpdateAction, updateLog.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Delete")]
    [InlineData("Modify")]
    public void Given_InvalidAction_When_CreatingMedicalRecordAuditLog_Then_ThrowsDomainException(string invalidAction)
    {
        var ex = Assert.Throws<DomainException>(() =>
            MedicalRecordAuditLog.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                invalidAction,
                "Motivo válido"));

        Assert.Contains("acción de auditoría médica es inválida", ex.Message);
    }

    [Fact]
    public void Given_EmptyJustificationOrIds_When_CreatingMedicalRecordAuditLog_Then_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            MedicalRecordAuditLog.CreateRead(Guid.Empty, Guid.NewGuid(), "Motivo válido"));

        Assert.Throws<DomainException>(() =>
            MedicalRecordAuditLog.CreateRead(Guid.NewGuid(), Guid.Empty, "Motivo válido"));

        Assert.Throws<DomainException>(() =>
            MedicalRecordAuditLog.CreateRead(Guid.NewGuid(), Guid.NewGuid(), "   "));

        var tooLongJustification = new string('A', MedicalRecordAuditLog.MaxJustificationLength + 1);
        Assert.Throws<DomainException>(() =>
            MedicalRecordAuditLog.CreateRead(Guid.NewGuid(), Guid.NewGuid(), tooLongJustification));
    }

    [Fact]
    public async Task Given_InsertedMedicalAuditLog_When_AttemptingSqlUpdateOrEfUpdate_Then_DatabaseRejectsOperationAndRecordRemainsIntact()
    {
        // arrange
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.MigrateAsync();

        var repository = new MedicalRecordAuditLogRepository(dbContext);
        var auditLog = MedicalRecordAuditLog.CreateRead(
            athleteId: athleteId,
            accessedByUserId: userId,
            justification: "Revisión médica por molestia articular reportada");

        await repository.AddAsync(auditLog);
        await dbContext.SaveChangesAsync();

        // act & assert 1: intento de ejecutar sentencia UPDATE directa en la base de datos (trigger append-only)
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE medical_record_audit_logs SET justification = 'Motivo adulterado', action = 'Update' WHERE id = {auditLog.Id}"));

        // act & assert 2: intento de modificar mediante EF Core ChangeTracker
        dbContext.Entry(auditLog).State = EntityState.Modified;
        await Assert.ThrowsAsync<DomainException>(async () =>
            await dbContext.SaveChangesAsync());

        // verificar que el registro permanece 100% íntegro en la base de datos
        await using var verifyContext = CreateDbContextForPf(pfId);
        var persistedLog = await verifyContext.MedicalRecordAuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(log => log.Id == auditLog.Id);

        Assert.NotNull(persistedLog);
        Assert.Equal(athleteId, persistedLog.AthleteId);
        Assert.Equal(userId, persistedLog.AccessedByUserId);
        Assert.Equal(pfId, persistedLog.PfId);
        Assert.Equal(MedicalRecordAuditLog.ReadAction, persistedLog.Action);
        Assert.Equal("Revisión médica por molestia articular reportada", persistedLog.Justification);
        Assert.Null(persistedLog.DeletedAt);
    }

    [Fact]
    public async Task Given_InsertedMedicalAuditLog_When_AttemptingSqlDeleteOrEfDelete_Then_DatabaseRejectsOperationAndRecordRemainsIntact()
    {
        // arrange
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.MigrateAsync();

        var repository = new MedicalRecordAuditLogRepository(dbContext);
        var auditLog = MedicalRecordAuditLog.CreateUpdate(
            athleteId: athleteId,
            accessedByUserId: userId,
            justification: "Carga de nuevo certificado de apto físico anual");

        await repository.AddAsync(auditLog);
        await dbContext.SaveChangesAsync();

        // act & assert 1: intento de ejecutar sentencia DELETE directa en la base de datos (trigger append-only)
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM medical_record_audit_logs WHERE id = {auditLog.Id}"));

        // act & assert 2: intento de eliminar (soft delete / Remove) desde EF Core
        dbContext.MedicalRecordAuditLogs.Remove(auditLog);
        await Assert.ThrowsAsync<DomainException>(async () =>
            await dbContext.SaveChangesAsync());

        // verificar que el registro sigue existiendo e íntegro en la base de datos
        await using var verifyContext = CreateDbContextForPf(pfId);
        var persistedLog = await verifyContext.MedicalRecordAuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(log => log.Id == auditLog.Id);

        Assert.NotNull(persistedLog);
        Assert.Equal(athleteId, persistedLog.AthleteId);
        Assert.Equal(userId, persistedLog.AccessedByUserId);
        Assert.Equal(pfId, persistedLog.PfId);
        Assert.Equal(MedicalRecordAuditLog.UpdateAction, persistedLog.Action);
        Assert.Equal("Carga de nuevo certificado de apto físico anual", persistedLog.Justification);
        Assert.Null(persistedLog.DeletedAt);
    }

    [Fact]
    public async Task Given_MultipleAuditLogsForDifferentPfs_When_ListingByAthleteId_Then_ReturnsOnlyCurrentPfLogsOrderedByCreatedAtDesc()
    {
        // arrange
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var dbContextPfA = CreateDbContextForPf(pfA);
        await dbContextPfA.Database.MigrateAsync();

        var repoPfA = new MedicalRecordAuditLogRepository(dbContextPfA);
        var firstLog = MedicalRecordAuditLog.CreateCreation(athleteId, userId, "Alta inicial de ficha médica");
        var secondLog = MedicalRecordAuditLog.CreateRead(athleteId, userId, "Control previo a partido");

        await repoPfA.AddAsync(firstLog);
        await repoPfA.AddAsync(secondLog);
        await dbContextPfA.SaveChangesAsync();

        // act & assert: PF-A ve sus 2 registros de auditoría
        var logsPfA = await repoPfA.ListByAthleteIdAsync(athleteId);
        Assert.Equal(2, logsPfA.Count);

        // aislamiento multi-tenant: PF-B no ve los registros de auditoría de PF-A
        await using var dbContextPfB = CreateDbContextForPf(pfB);
        var repoPfB = new MedicalRecordAuditLogRepository(dbContextPfB);
        var logsPfB = await repoPfB.ListByAthleteIdAsync(athleteId);
        Assert.Empty(logsPfB);
    }

    [Fact]
    public void Given_PostgreSqlMigration_When_GeneratingSqlForMedicalRecordAuditLogs_Then_IncludesAppendOnlyPlPgSqlFunctionAndTrigger()
    {
        // arrange: instanciar la migración contra el proveedor Npgsql de PostgreSQL
        var migration = new global::Infrastructure.Data.Migrations.AddMedicalRecordAuditLogsTable();
        var migrationBuilder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        var upMethod = typeof(global::Infrastructure.Data.Migrations.AddMedicalRecordAuditLogsTable)
            .GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        // act
        upMethod.Invoke(migration, new object[] { migrationBuilder });

        var sqlOperations = migrationBuilder.Operations
            .OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()
            .Select(op => op.Sql)
            .ToList();

        // assert: verifica que en PostgreSQL se crean la función PL/pgSQL y los triggers BEFORE UPDATE OR DELETE y BEFORE TRUNCATE
        Assert.Single(sqlOperations);
        var triggerSql = sqlOperations[0];
        Assert.Contains("CREATE OR REPLACE FUNCTION prevent_medical_record_audit_logs_modification()", triggerSql);
        Assert.Contains("RAISE EXCEPTION", triggerSql);
        Assert.Contains("BEFORE UPDATE OR DELETE ON medical_record_audit_logs", triggerSql);
        Assert.Contains("EXECUTE FUNCTION prevent_medical_record_audit_logs_modification()", triggerSql);
        Assert.Contains("BEFORE TRUNCATE ON medical_record_audit_logs", triggerSql);
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
