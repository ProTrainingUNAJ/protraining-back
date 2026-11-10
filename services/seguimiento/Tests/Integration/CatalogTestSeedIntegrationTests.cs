using Domain.Entities;
using Infrastructure.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// KAN-334 (tarea #039): el arranque del microservicio de Seguimiento debe sembrar
// el catálogo de pruebas físicas con las unidades de medida correctas, para que el
// Preparador Físico siempre encuentre las pruebas requeridas (CMJ, Sentadilla 1RM,
// Bronco e Illinois, entre otras) sin depender de carga manual.
public sealed class CatalogTestSeedIntegrationTests
{
    [Fact]
    public async Task SeedAsync_OnEmptyDatabase_SeedsTheFullCatalogAsGlobalAndActive()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync();

        var tests = await factory.ExecuteInDbContextAsync(
            // Saltamos el filtro global únicamente para verificar el catálogo global
            // con PfId = Guid.Empty, que debe quedar visible para todos los PF.
            db => db.CatalogTests.IgnoreQueryFilters().ToListAsync());

        Assert.Equal(17, tests.Count);
        Assert.All(tests, test => Assert.True(test.Active));
        // El seeder corre sin token: PfId = Guid.Empty las vuelve catálogo global.
        Assert.All(tests, test => Assert.Equal(Guid.Empty, test.PfId));
    }

    [Theory]
    [InlineData("CMJ", "cm", MotorCapacity.Power)]
    [InlineData("Abalakov", "cm", MotorCapacity.Power)]
    [InlineData("Sentadilla 1RM", "kg", MotorCapacity.Strength)]
    [InlineData("Bronco", "m", MotorCapacity.Endurance)]
    [InlineData("Illinois", "s", MotorCapacity.Agility)]
    // KAN-458: tests de carrera con su unidad en el catálogo.
    [InlineData("Carrera 30 m", "s", MotorCapacity.Velocity)]
    [InlineData("Carrera 100 m", "s", MotorCapacity.Velocity)]
    public async Task SeedAsync_RequiredTests_AreSeededWithTheExpectedMeasurementUnit(
        string nameFragment, string expectedUnit, MotorCapacity expectedCapacity)
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync();

        var tests = await factory.ExecuteInDbContextAsync(
            // Saltamos el filtro global únicamente para verificar el catálogo global
            // con PfId = Guid.Empty, que debe quedar visible para todos los PF.
            db => db.CatalogTests.IgnoreQueryFilters().ToListAsync());

        var test = Assert.Single(tests.Where(t => t.Name.Contains(nameFragment)));
        Assert.Equal(expectedUnit, test.MeasurementUnit);
        Assert.Equal(expectedCapacity, test.MotorCapacity);
    }

    [Fact]
    public async Task SeedAsync_AllSeededTests_HaveConsistentUnitsAndDifficultyLevels()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync();

        var tests = await factory.ExecuteInDbContextAsync(
            // Saltamos el filtro global únicamente para verificar el catálogo global
            // con PfId = Guid.Empty, que debe quedar visible para todos los PF.
            db => db.CatalogTests.IgnoreQueryFilters().ToListAsync());

        Assert.All(tests, test => Assert.False(string.IsNullOrWhiteSpace(test.MeasurementUnit)));
        Assert.All(tests, test => Assert.InRange(test.DifficultyLevel, 1, 5));
    }

    [Fact]
    public async Task SeedAsync_WhenCatalogIsAlreadySeeded_DoesNotDuplicateTheTests()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync();

        await factory.ExecuteInDbContextAsync(
            db => CatalogTestSeeder.SeedAsync(db, NullLogger.Instance));

        var tests = await factory.ExecuteInDbContextAsync(
            // Saltamos el filtro global únicamente para verificar el catálogo global
            // con PfId = Guid.Empty, que debe quedar visible para todos los PF.
            db => db.CatalogTests.IgnoreQueryFilters().ToListAsync());

        // KAN-458: backfill idempotente, el catálogo no crece en una segunda corrida.
        Assert.Equal(17, tests.Count);
    }

    // KAN-458: una base sembrada antes de este ticket no repite el alta inicial
    // (early return), por lo que el backfill debe agregar el test nuevo sin duplicar.
    [Fact]
    public async Task SeedAsync_WhenCatalogWasSeededBefore_AddsTheMissingTests()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync();

        // Se simula una base anterior: se elimina el test de 100 m.
        await factory.ExecuteInDbContextAsync(async db =>
        {
            var test = await db.CatalogTests.IgnoreQueryFilters()
                .SingleAsync(t => t.Name == "Carrera 100 m");
            db.CatalogTests.Remove(test);
            await db.SaveChangesAsync();
        });

        await factory.ExecuteInDbContextAsync(
            db => CatalogTestSeeder.SeedAsync(db, NullLogger.Instance));

        var tests = await factory.ExecuteInDbContextAsync(
            // Saltamos el filtro global únicamente para verificar el catálogo global
            // con PfId = Guid.Empty, que debe quedar visible para todos los PF.
            db => db.CatalogTests.IgnoreQueryFilters().ToListAsync());

        Assert.Equal(17, tests.Count);
        var carrera100 = Assert.Single(tests.Where(t => t.Name == "Carrera 100 m"));
        Assert.Equal("s", carrera100.MeasurementUnit);
        Assert.Equal(MotorCapacity.Velocity, carrera100.MotorCapacity);
        Assert.Equal(Guid.Empty, carrera100.PfId);
    }
}
