using Application.Exceptions;
using Application.Interfaces.Services;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Estructura.Tests;

public sealed class SeasonPhaseOverlapTests : IAsyncDisposable
{
    private readonly SqliteConnection _sqliteConnection;

    public SeasonPhaseOverlapTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    [Fact]
    public async Task Given_InvalidDateRange_When_ValidatingPhaseDates_Then_ThrowsDomainExceptionWithNeutralSpanishMessage()
    {
        // arrange
        var pfId = Guid.NewGuid();
        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.EnsureCreatedAsync();

        var service = CreateValidationService(dbContext);

        // act & assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.ValidatePhaseDatesAsync(
                Guid.NewGuid(),
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 3, 31)));

        Assert.Equal("La fecha de finalización no puede ser anterior a la fecha de inicio.", ex.Message);
    }

    [Fact]
    public async Task Given_NonExistentOrCrossTenantCategory_When_ValidatingPhaseDates_Then_ThrowsCategoryNotFoundException()
    {
        // arrange: PF A intenta asociar fase a una categoría de PF B
        var pfAId = Guid.NewGuid();
        var pfBId = Guid.NewGuid();

        await using (var seedContext = CreateDbContextForPf(pfBId))
        {
            await seedContext.Database.EnsureCreatedAsync();
            var org = Organization.Create("Club PF B");
            seedContext.Organizations.Add(org);
            await seedContext.SaveChangesAsync();

            var categoryOfPfB = Category.Create(org.Id, "Categoría PF B");
            seedContext.Categories.Add(categoryOfPfB);
            await seedContext.SaveChangesAsync();

            // act & assert: PF A consulta y no puede ver la categoría de PF B (filtro multitenant)
            await using var pfAContext = CreateDbContextForPf(pfAId);
            var service = CreateValidationService(pfAContext);

            await Assert.ThrowsAsync<CategoryNotFoundException>(() =>
                service.ValidatePhaseDatesAsync(
                    categoryOfPfB.Id,
                    new DateOnly(2026, 1, 1),
                    new DateOnly(2026, 3, 31)));
        }
    }

    [Fact]
    public async Task Given_ExistingPhase_When_NewPhaseOverlaps_Then_ThrowsDomainExceptionWithNeutralSpanishMessage()
    {
        // arrange: categoría con fase existente del 01-01 al 31-03
        var pfId = Guid.NewGuid();
        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.EnsureCreatedAsync();

        var org = Organization.Create("Club Atlético");
        dbContext.Organizations.Add(org);
        await dbContext.SaveChangesAsync();

        var category = Category.Create(org.Id, "Primera División");
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        var existingPhase = SeasonPhase.Create(
            category.Id,
            "Pretemporada",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));
        dbContext.SeasonPhases.Add(existingPhase);
        await dbContext.SaveChangesAsync();

        var service = CreateValidationService(dbContext);

        // act & assert: intento de crear fase del 15-03 al 01-05 (solapamiento al inicio)
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.ValidatePhaseDatesAsync(
                category.Id,
                new DateOnly(2026, 3, 15),
                new DateOnly(2026, 5, 1)));

        Assert.Equal("Las fechas de la fase se superponen con otra fase de la misma categoría.", ex.Message);
    }

    [Fact]
    public async Task Given_ExistingPhase_When_NewPhaseIsContainedWithinExisting_Then_ThrowsDomainException()
    {
        // arrange
        var pfId = Guid.NewGuid();
        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.EnsureCreatedAsync();

        var org = Organization.Create("Club Atlético");
        dbContext.Organizations.Add(org);
        await dbContext.SaveChangesAsync();

        var category = Category.Create(org.Id, "Reserva");
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        var existingPhase = SeasonPhase.Create(
            category.Id,
            "Fase Competitiva Larga",
            SeasonPhaseType.Competitive,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 30));
        dbContext.SeasonPhases.Add(existingPhase);
        await dbContext.SaveChangesAsync();

        var service = CreateValidationService(dbContext);

        // act & assert: intento de crear fase dentro del rango (del 01-03 al 31-03)
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.ValidatePhaseDatesAsync(
                category.Id,
                new DateOnly(2026, 3, 1),
                new DateOnly(2026, 3, 31)));

        Assert.Equal("Las fechas de la fase se superponen con otra fase de la misma categoría.", ex.Message);
    }

    [Fact]
    public async Task Given_ExistingPhase_When_NewPhaseIsAdjacentWithoutOverlap_Then_ValidationSucceeds()
    {
        // arrange
        var pfId = Guid.NewGuid();
        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.EnsureCreatedAsync();

        var org = Organization.Create("Club Atlético");
        dbContext.Organizations.Add(org);
        await dbContext.SaveChangesAsync();

        var category = Category.Create(org.Id, "Juveniles");
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        var existingPhase = SeasonPhase.Create(
            category.Id,
            "Pretemporada",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));
        dbContext.SeasonPhases.Add(existingPhase);
        await dbContext.SaveChangesAsync();

        var service = CreateValidationService(dbContext);

        // act & assert: fase adyacente del 01-04 al 30-06 (no hay superposición de días)
        var exception = await Record.ExceptionAsync(() =>
            service.ValidatePhaseDatesAsync(
                category.Id,
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 6, 30)));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Given_PhasesInDifferentCategories_When_DatesOverlap_Then_ValidationSucceeds()
    {
        // arrange
        var pfId = Guid.NewGuid();
        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.EnsureCreatedAsync();

        var org = Organization.Create("Club Atlético");
        dbContext.Organizations.Add(org);
        await dbContext.SaveChangesAsync();

        var category1 = Category.Create(org.Id, "Categoría A");
        var category2 = Category.Create(org.Id, "Categoría B");
        dbContext.Categories.AddRange(category1, category2);
        await dbContext.SaveChangesAsync();

        var phase1 = SeasonPhase.Create(
            category1.Id,
            "Pretemporada Cat A",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));
        dbContext.SeasonPhases.Add(phase1);
        await dbContext.SaveChangesAsync();

        var service = CreateValidationService(dbContext);

        // act & assert: misma fecha pero en Categoría B -> debe permitirse
        var exception = await Record.ExceptionAsync(() =>
            service.ValidatePhaseDatesAsync(
                category2.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 3, 31)));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Given_ExistingPhaseSoftDeleted_When_NewPhaseHasOverlappingDates_Then_ValidationSucceeds()
    {
        // arrange
        var pfId = Guid.NewGuid();
        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.EnsureCreatedAsync();

        var org = Organization.Create("Club Atlético");
        dbContext.Organizations.Add(org);
        await dbContext.SaveChangesAsync();

        var category = Category.Create(org.Id, "Fútbol Femenino");
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        var phase = SeasonPhase.Create(
            category.Id,
            "Fase a Eliminar",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));
        dbContext.SeasonPhases.Add(phase);
        await dbContext.SaveChangesAsync();

        // soft delete mediante EF Core
        dbContext.SeasonPhases.Remove(phase);
        await dbContext.SaveChangesAsync();

        var service = CreateValidationService(dbContext);

        // act & assert: crear nueva fase con el mismo rango exacto que la eliminada lógicamente
        var exception = await Record.ExceptionAsync(() =>
            service.ValidatePhaseDatesAsync(
                category.Id,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 3, 31)));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Given_UpdatingCurrentPhase_When_CheckingOverlapAgainstSelf_Then_AllowsUpdate()
    {
        // arrange
        var pfId = Guid.NewGuid();
        await using var dbContext = CreateDbContextForPf(pfId);
        await dbContext.Database.EnsureCreatedAsync();

        var org = Organization.Create("Club Atlético");
        dbContext.Organizations.Add(org);
        await dbContext.SaveChangesAsync();

        var category = Category.Create(org.Id, "Infantiles");
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        var currentPhase = SeasonPhase.Create(
            category.Id,
            "Fase Actual",
            SeasonPhaseType.Competitive,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 7, 31));
        dbContext.SeasonPhases.Add(currentPhase);
        await dbContext.SaveChangesAsync();

        var service = CreateValidationService(dbContext);

        // act & assert: excluir el ID de la fase actual permite actualizar su rango a 10-04 al 15-08
        var exception = await Record.ExceptionAsync(() =>
            service.ValidatePhaseDatesAsync(
                category.Id,
                new DateOnly(2026, 4, 10),
                new DateOnly(2026, 8, 15),
                excludePhaseId: currentPhase.Id));

        Assert.Null(exception);
    }

    private static SeasonPhaseValidationService CreateValidationService(AppDbContext context)
    {
        var categoryRepo = new CategoryRepository(context);
        var seasonPhaseRepo = new SeasonPhaseRepository(context);
        return new SeasonPhaseValidationService(categoryRepo, seasonPhaseRepo);
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
